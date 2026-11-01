using StoryPlatform.Application.Features.ListeningInteractions.Models;
using StoryPlatform.Application.Features.MediaGeneration.Models;
using StoryPlatform.Contracts.AI.Models;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ListeningInteractions.Services;

/// <summary>
/// Luật cố định cho tương tác lúc nghe (spec mục 5.3). Phase 4, duyệt và kiểm tra trước khi duyệt cùng dùng.
/// Mã lỗi và tên field khớp JSON của AI để gửi lại qua previousErrors.
/// </summary>
public static partial class ListeningInteractionRules
{
    public static readonly IReadOnlyList<string> DefaultGradingWords = ["sai", "chưa đúng", "không đúng"];
    public const int RolePlayMaxWords = 12;
    public const int FillBlankMaxWords = 15;
    public const int EventMaxWords = 10;

    public static IReadOnlyList<ListeningValidationError> Validate(
        IReadOnlyList<ListeningInteractionDraft> items, ListeningRuleContext context)
    {
        var errors = new List<ListeningValidationError>();
        if (items.Count != context.Count) errors.Add(new("COUNT_MISMATCH"));
        var seenAnchors = new HashSet<int>();
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (item.Type != context.Type) errors.Add(new("SCHEMA_INVALID", i, "type"));
            ValidateFields(item, i, errors);
            ValidateAnchor(item, i, context, seenAnchors, errors);
            switch (item.Type)
            {
                case ListeningInteractionType.Checkpoint: ValidateCheckpoint(item, i, errors); break;
                case ListeningInteractionType.RolePlay: ValidateRolePlay(item, i, context, errors); break;
                case ListeningInteractionType.FillBlank: ValidateFillBlankStructure(item, i, errors); break;
                case ListeningInteractionType.EventOrdering: ValidateOrdering(item, i, errors); break;
                case ListeningInteractionType.PicturePrediction: ValidatePicture(item, i, errors); break;
            }
            errors.AddRange(ValidateText(item, i, context));
        }
        if (context.Type == ListeningInteractionType.Checkpoint)
        {
            var kinds = items.Where(x => x.Kind is not null).GroupBy(x => x.Kind!.Value).ToArray();
            if (kinds.Any(g => g.Count() > 2) ||
                (items.Count > 0 && !items.Any(x => x.Kind is CheckpointKind.Emotion or CheckpointKind.Opinion)))
                errors.Add(new("KIND_LIMIT"));
        }
        return errors.Distinct().ToArray();
    }

    /// <summary>Luật về chữ: dùng cả khi người duyệt sửa một tương tác (P4).</summary>
    public static IReadOnlyList<ListeningValidationError> ValidateText(
        ListeningInteractionDraft item, int index, ListeningRuleContext context)
    {
        var errors = new List<ListeningValidationError>();
        foreach (var (field, value) in RequiredFeedback(item))
            if (string.IsNullOrWhiteSpace(value)) errors.Add(new("FEEDBACK_MISSING", index, field));

        var gradable = TextFields(item).Where(x => x.Value is not null && x.Field != "explanation" &&
            !(x.Field == "matchFeedback" && item.Type is ListeningInteractionType.FillBlank or ListeningInteractionType.EventOrdering));
        foreach (var (field, value) in gradable)
            if (ContainsGradingWord(value!, context.GradingWords)) errors.Add(new("GRADING_WORD", index, field));

        foreach (var (field, value) in TextFields(item))
            if (value is not null && !context.IsSafe(value)) errors.Add(new("UNSAFE_CONTENT", index, field));

        // Chỉ xét câu hỏi: thẻ cảm xúc ("Thỏ đang sợ") hay hình đoán sai ("Một bạn sóc nhỏ") vốn không có trong truyện;
        // sự kiện đã được bám truyện bằng câu trích nguyên văn.
        if (item.Prompt is not null && !LearningArtifactGrounding.HasRelevantTerm(item.Prompt, context.GroundingSource))
            errors.Add(new("NOT_GROUNDED", index, "question"));

        if (item.Type == ListeningInteractionType.EventOrdering)
            for (var j = 0; j < item.Options.Count; j++)
                if (CountWords(item.Options[j].Text) > EventMaxWords) errors.Add(new("LENGTH_LIMIT", index, $"events[{j}].text"));

        if (item.Type == ListeningInteractionType.FillBlank && item.HiddenWord is { Length: > 0 } hidden)
        {
            if (item.Hint is not null && item.Hint.Contains(hidden, StringComparison.OrdinalIgnoreCase))
                errors.Add(new("BLANK_INVALID", index, "hint"));
            if (!item.Options.Any(o => Normalize(o.Text) == Normalize(hidden)))
                errors.Add(new("BLANK_INVALID", index, "acceptedAnswers"));
        }
        return errors;
    }

    /// <summary>Nội dung version đổi sau khi lưu: neo phải vẫn là cuối một đoạn, câu trích phải vẫn đúng vị trí.</summary>
    public static IReadOnlyList<ListeningValidationError> CheckStale(
        ListeningInteractionDraft item, int index, string content, IReadOnlyList<StoryTextBlock> blocks)
    {
        var ends = blocks.Select(b => b.EndOffset).ToHashSet();
        var stale = item.AnchorOffset is not { } anchor || !ends.Contains(anchor) || !LastBlockRuleHolds(item, anchor, blocks) ||
                    (item.RevealOffset is { } reveal && !ends.Contains(reveal)) ||
                    !QuoteAt(content, item.SourceQuote, item.SourceStart, item.SourceEnd) ||
                    item.Options.Any(o => !QuoteAt(content, o.SourceQuote, o.SourceStart, o.SourceEnd));
        return stale ? [new("STALE_ANCHOR", index)] : [];
    }

    public static int CountWords(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Count(token => token.Any(char.IsLetterOrDigit));

    /// <summary>So theo nguyên tiếng, không phân biệt hoa thường: "sai" chặn "Sai rồi" nhưng không chặn "Sải".</summary>
    public static bool ContainsGradingWord(string text, IReadOnlyCollection<string> words)
    {
        var syllables = Syllables(text);
        foreach (var word in words)
        {
            var target = Syllables(word);
            if (target.Length == 0) continue;
            for (var i = 0; i + target.Length <= syllables.Length; i++)
                if (syllables.AsSpan(i, target.Length).SequenceEqual(target)) return true;
        }
        return false;
    }

    private static string[] Syllables(string text) =>
        new string(text.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray())
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private static string Normalize(string value) =>
        string.Join(' ', value.Trim().ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static bool QuoteAt(string content, string? quote, int? start, int? end) =>
        quote is null || (start is { } s && end is { } e && s >= 0 && e <= content.Length && s < e && content[s..e] == quote);

    private static void ValidateAnchor(ListeningInteractionDraft item, int index, ListeningRuleContext context,
        HashSet<int> seen, List<ListeningValidationError> errors)
    {
        if (item.AnchorOffset is not { } anchor) return; // mapper đã báo ANCHOR_INVALID
        var isBlockEnd = context.Blocks.Any(b => b.EndOffset == anchor);
        if (!isBlockEnd || !LastBlockRuleHolds(item, anchor, context.Blocks) ||
            context.OccupiedAnchorOffsets.Contains(anchor) || !seen.Add(anchor))
            errors.Add(new("ANCHOR_INVALID", index, "afterBlockId"));
        // Mỗi mục một lỗi neo: chỉ xét vùng khi neo đã hợp lệ theo luật chung.
        else if (index < context.Zones.Count && !context.Zones[index].Contains(item.AnchorBlockId))
            errors.Add(new("ANCHOR_OUT_OF_ZONE", index, "afterBlockId"));
    }

    // Chỉ EventOrdering được, và phải, đặt sau đoạn cuối (trẻ đã nghe hết truyện).
    private static bool LastBlockRuleHolds(ListeningInteractionDraft item, int anchor, IReadOnlyList<StoryTextBlock> blocks) =>
        (anchor == blocks[^1].EndOffset) == (item.Type == ListeningInteractionType.EventOrdering);

    private static void ValidateCheckpoint(ListeningInteractionDraft item, int index, List<ListeningValidationError> errors)
    {
        var count = item.Options.Count;
        var countOk = item.Kind == CheckpointKind.Emotion ? count == 4 : count is >= 2 and <= 3;
        if (!countOk || HasDuplicateText(item)) errors.Add(new("OPTION_COUNT_INVALID", index, "options"));
        var matches = item.Options.Count(o => o.IsMatch);
        if (item.Kind == CheckpointKind.Opinion ? matches != 0 : matches != 1) errors.Add(new("MATCH_RULE_INVALID", index, "options"));
        if (item.SourceEnd is { } end && item.AnchorOffset is { } anchor && end > anchor)
            errors.Add(new("QUOTE_AFTER_ANCHOR", index, "sourceQuote"));
    }

    private static void ValidatePicture(ListeningInteractionDraft item, int index, List<ListeningValidationError> errors)
    {
        if (item.Options.Count is < 2 or > 3 || HasDuplicateText(item)) errors.Add(new("OPTION_COUNT_INVALID", index, "options"));
        if (item.Options.Count(o => o.IsMatch) != 1) errors.Add(new("MATCH_RULE_INVALID", index, "options"));
        for (var j = 0; j < item.Options.Count; j++)
            if (string.IsNullOrWhiteSpace(item.Options[j].ImageDescription))
                errors.Add(new("SCHEMA_INVALID", index, $"options[{j}].imageDescription"));
        if (item.RevealOffset is { } reveal && item.AnchorOffset is { } anchor && reveal <= anchor)
            errors.Add(new("REVEAL_BLOCK_INVALID", index, "revealBlockId"));
        if (item.SourceEnd is { } end && item.AnchorOffset is { } a && end > a)
            errors.Add(new("QUOTE_AFTER_ANCHOR", index, "sourceQuote"));
    }

    private static void ValidateRolePlay(ListeningInteractionDraft item, int index, ListeningRuleContext context,
        List<ListeningValidationError> errors)
    {
        if (!context.DialogueBlockIds.Contains(item.AnchorBlockId)) errors.Add(new("DIALOGUE_BLOCK_INVALID", index, "afterBlockId"));
        if (string.IsNullOrWhiteSpace(item.CharacterName)) errors.Add(new("SCHEMA_INVALID", index, "characterName"));
        if (item.SourceQuote is not null && CountWords(item.SourceQuote) > RolePlayMaxWords)
            errors.Add(new("LENGTH_LIMIT", index, "lineQuote"));
    }

    private static void ValidateFillBlankStructure(ListeningInteractionDraft item, int index, List<ListeningValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(item.HiddenWord) || item.SourceQuote is null ||
            !item.SourceQuote.Contains(item.HiddenWord, StringComparison.OrdinalIgnoreCase))
            errors.Add(new("BLANK_INVALID", index, "hiddenWord"));
        if (item.SourceQuote is not null && CountWords(item.SourceQuote) > FillBlankMaxWords)
            errors.Add(new("LENGTH_LIMIT", index, "sentenceQuote"));
    }

    private static void ValidateOrdering(ListeningInteractionDraft item, int index, List<ListeningValidationError> errors)
    {
        var events = item.Options.OrderBy(o => o.CorrectOrder).ToArray();
        var starts = events.Select(o => o.SourceStart).ToArray();
        var blocks = events.Select(o => o.BlockId).ToArray();
        var ok = events.Length == 3 &&
                 events.Select(o => o.CorrectOrder).SequenceEqual([1, 2, 3]) &&
                 starts.All(s => s is not null) &&
                 starts[0] < starts[1] && starts[1] < starts[2] &&
                 blocks.Distinct().Count() == 3 &&
                 item.Options.Select(o => o.SourceStart).SequenceEqual(starts); // thứ tự AI trả = thứ tự truyện
        if (!ok) errors.Add(new("EVENT_ORDER_INVALID", index, "events"));
    }

    private static bool HasDuplicateText(ListeningInteractionDraft item) =>
        item.Options.Select(o => Normalize(o.Text)).Distinct().Count() != item.Options.Count;

    private static string OptionField(ListeningInteractionDraft item, int j, string name) =>
        item.Type == ListeningInteractionType.EventOrdering ? $"events[{j}].{name}" :
        item.Type == ListeningInteractionType.PicturePrediction && name == "text" ? $"options[{j}].label" : $"options[{j}].{name}";

    // Các trường chữ của tương tác và option, kèm tên field JSON.
    private static IEnumerable<(string Field, string? Value)> TextFields(ListeningInteractionDraft item)
    {
        yield return ("question", item.Prompt);
        yield return ("characterName", item.CharacterName);
        yield return ("hint", item.Hint);
        yield return ("explanation", item.Explanation);
        yield return ("matchFeedback", item.MatchFeedback);
        yield return ("noMatchFeedback", item.NoMatchFeedback);
        yield return ("waitFeedback", item.WaitFeedback);
        for (var j = 0; j < item.Options.Count; j++)
        {
            yield return (OptionField(item, j, "text"), item.Options[j].Text);
            yield return (OptionField(item, j, "feedback"), item.Options[j].Feedback);
            yield return (OptionField(item, j, "imageDescription"), item.Options[j].ImageDescription);
        }
    }

    private static IEnumerable<(string Field, string? Value)> RequiredFeedback(ListeningInteractionDraft item)
    {
        switch (item.Type)
        {
            case ListeningInteractionType.Checkpoint:
                yield return ("otherFeedback", item.NoMatchFeedback);
                for (var j = 0; j < item.Options.Count; j++) yield return ($"options[{j}].feedback", item.Options[j].Feedback);
                break;
            case ListeningInteractionType.RolePlay:
                yield return ("praiseFeedback", item.MatchFeedback);
                break;
            case ListeningInteractionType.FillBlank:
                yield return ("matchFeedback", item.MatchFeedback);
                yield return ("suggestFeedback", item.NoMatchFeedback);
                break;
            case ListeningInteractionType.EventOrdering:
                yield return ("matchFeedback", item.MatchFeedback);
                yield return ("retryFeedback", item.NoMatchFeedback);
                break;
            case ListeningInteractionType.PicturePrediction:
                yield return ("waitFeedback", item.WaitFeedback);
                for (var j = 0; j < item.Options.Count; j++) yield return ($"options[{j}].feedback", item.Options[j].Feedback);
                break;
        }
    }

    // Cột không thuộc Type phải để trống (spec mục 3.1).
    private static void ValidateFields(ListeningInteractionDraft item, int index, List<ListeningValidationError> errors)
    {
        var t = item.Type;
        void Forbid(bool present, string field)
        {
            if (present) errors.Add(new("SCHEMA_INVALID", index, field));
        }
        Forbid(item.Kind is not null && t != ListeningInteractionType.Checkpoint, "kind");
        Forbid(item.Prompt is not null && t is not (ListeningInteractionType.Checkpoint or ListeningInteractionType.PicturePrediction), "question");
        Forbid(item.SourceQuote is not null && t == ListeningInteractionType.EventOrdering, "sourceQuote");
        Forbid(item.CharacterName is not null && t != ListeningInteractionType.RolePlay, "characterName");
        Forbid(item.HiddenWord is not null && t != ListeningInteractionType.FillBlank, "hiddenWord");
        Forbid(item.Hint is not null && t != ListeningInteractionType.FillBlank, "hint");
        Forbid(item.RevealOffset is not null && t != ListeningInteractionType.PicturePrediction, "revealBlockId");
        Forbid(item.Explanation is not null && t != ListeningInteractionType.Checkpoint, "explanation");
        Forbid(item.MatchFeedback is not null && t is ListeningInteractionType.Checkpoint or ListeningInteractionType.PicturePrediction, "matchFeedback");
        Forbid(item.NoMatchFeedback is not null && t is ListeningInteractionType.RolePlay or ListeningInteractionType.PicturePrediction, "noMatchFeedback");
        Forbid(item.WaitFeedback is not null && t != ListeningInteractionType.PicturePrediction, "waitFeedback");
        Forbid(item.Options.Count > 0 && t == ListeningInteractionType.RolePlay, "options");
        Forbid(t == ListeningInteractionType.Checkpoint && item.Kind is null, "kind");
        Forbid(t is ListeningInteractionType.Checkpoint or ListeningInteractionType.PicturePrediction && string.IsNullOrWhiteSpace(item.Prompt), "question");
        for (var j = 0; j < item.Options.Count; j++)
        {
            var o = item.Options[j];
            Forbid(o.Feedback is not null && t is ListeningInteractionType.FillBlank or ListeningInteractionType.EventOrdering, OptionField(item, j, "feedback"));
            Forbid(o.IsMatch && t is ListeningInteractionType.FillBlank or ListeningInteractionType.EventOrdering, OptionField(item, j, "isMatch"));
            Forbid(o.CorrectOrder is not null && t != ListeningInteractionType.EventOrdering, OptionField(item, j, "order"));
            Forbid(o.SourceQuote is not null && t != ListeningInteractionType.EventOrdering, OptionField(item, j, "sourceQuote"));
            Forbid(o.ImageDescription is not null && t != ListeningInteractionType.PicturePrediction, OptionField(item, j, "imageDescription"));
            Forbid(string.IsNullOrWhiteSpace(o.Text), OptionField(item, j, "text"));
        }
    }
}
