using StoryPlatform.Application.Features.ListeningInteractions.Models;
using StoryPlatform.Contracts.AI.Models;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ListeningInteractions.Services;

public static partial class ListeningInteractionRules
{
    public const int AlternateLineMaxWords = 12;
    public const int AlternateMinLines = 2, AlternateMaxLines = 4;
    public const int PassageMaxWords = 40, PassageMinSentences = 2, PassageMaxSentences = 3;
    public const int InferenceAnswerMaxWords = 12, ClueCount = 3;
    public const int CauseCardMaxWords = 10;
    public const int SceneTitleMaxWords = 8;
    public const int ThoughtMaxWords = 12;

    /// <summary>Cảm xúc của bong bóng suy nghĩ (danh sách cố định để frontend có hoạt cảnh tương ứng).</summary>
    public static IReadOnlyList<string> Emotions => ListeningAgeCatalog.ThoughtEmotions;

    private static int PositionBefore(string content, string quote, int? anchor)
    {
        var at = content.IndexOf(quote, StringComparison.Ordinal);
        return at >= 0 && anchor is { } a && at + quote.Length <= a ? at : -1;
    }

    private static int SentenceCount(string text) =>
        text.Split(['.', '!', '?', '…'], StringSplitOptions.RemoveEmptyEntries).Count(s => s.Any(char.IsLetterOrDigit));

    private static void ValidateAlternateReading(ListeningInteractionDraft item, int index, ListeningRuleContext context,
        List<ListeningValidationError> errors)
    {
        if (!context.DialogueBlockIds.Contains(item.AnchorBlockId)) errors.Add(new("DIALOGUE_BLOCK_INVALID", index, "afterBlockId"));
        var lines = item.Options.OrderBy(o => o.CorrectOrder).ToArray();
        if (lines.Length is < AlternateMinLines or > AlternateMaxLines) { errors.Add(new("OPTION_COUNT_INVALID", index, "options")); return; }
        if (!lines.Select(o => o.CorrectOrder).SequenceEqual(Enumerable.Range(1, lines.Length).Select(i => (int?)i)))
            errors.Add(new("LINE_ORDER_INVALID", index, "options"));

        var speakers = lines.Select(l => l.Speaker?.Trim() ?? string.Empty).ToArray();
        if (speakers.Any(string.IsNullOrWhiteSpace) || speakers.Any(s => CountWords(s) > 3) || speakers.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 2)
            errors.Add(new("SPEAKER_INVALID", index, "options"));
        for (var j = 0; j < item.Options.Count; j++)
            if (item.Options[j].Speaker is { Length: > 0 } speaker && !AppearsBefore(context.Content, speaker, item.AnchorOffset))
                errors.Add(new("SPEAKER_INVALID", index, $"options[{j}].speaker"));

        var previous = -1;
        for (var j = 0; j < item.Options.Count; j++)
        {
            var text = item.Options[j].Text;
            if (CountWords(text) > AlternateLineMaxWords) errors.Add(new("LENGTH_LIMIT", index, $"options[{j}].text"));
            var block = context.Blocks.FirstOrDefault(b => b.Text.Contains(text, StringComparison.Ordinal) && b.EndOffset <= item.AnchorOffset);
            if (block is null || !IsInsideQuotes(block.Text, text)) errors.Add(new("NOT_GROUNDED", index, $"options[{j}].text"));
        }
        foreach (var line in lines)
        {
            var at = context.Content.IndexOf(line.Text, previous + 1, StringComparison.Ordinal);
            if (at < 0) { errors.Add(new("LINE_ORDER_INVALID", index, "options")); break; }
            previous = at;
        }
    }

    private static void ValidateInferenceQuestion(ListeningInteractionDraft item, int index, ListeningRuleContext context,
        List<ListeningValidationError> errors)
    {
        var passage = item.SourceQuote?.Trim();
        if (string.IsNullOrWhiteSpace(passage) || PositionBefore(context.Content, passage, item.AnchorOffset) < 0)
            errors.Add(new("NOT_GROUNDED", index, "passage"));
        else if (CountWords(passage) > PassageMaxWords || SentenceCount(passage) is < PassageMinSentences or > PassageMaxSentences)
            errors.Add(new("LENGTH_LIMIT", index, "passage"));
        if (string.IsNullOrWhiteSpace(item.Prompt) || !item.Prompt.TrimEnd().EndsWith('?')) errors.Add(new("SCHEMA_INVALID", index, "question"));

        var answers = item.Options.Where(o => o.Role == ListeningOptionRole.None).ToArray();
        var clues = item.Options.Where(o => o.Role == ListeningOptionRole.Clue).ToArray();
        if (answers.Length != 3 || answers.Select(o => Normalize(o.Text)).Distinct().Count() != answers.Length)
            errors.Add(new("OPTION_COUNT_INVALID", index, "options"));
        if (answers.Count(o => o.IsMatch) != 1 || clues.Any(o => o.IsMatch)) errors.Add(new("MATCH_RULE_INVALID", index, "options"));
        for (var j = 0; j < item.Options.Count; j++)
            if (item.Options[j].Role == ListeningOptionRole.None && CountWords(item.Options[j].Text) > InferenceAnswerMaxWords)
                errors.Add(new("LENGTH_LIMIT", index, $"options[{j}].text"));
        var correct = answers.FirstOrDefault(o => o.IsMatch);
        if (correct is not null && passage is not null && CountWords(correct.Text) >= 3 &&
            passage.Contains(correct.Text.Trim().TrimEnd('.', '!', '?'), StringComparison.OrdinalIgnoreCase))
            errors.Add(new("NOT_INFERENCE", index, "options"));
        if (clues.Length != ClueCount || passage is null || ClueSpans.Find(passage, clues.Select(c => c.Text).ToArray()) is null)
            errors.Add(new("CLUE_INVALID", index, "clues"));
        if (string.IsNullOrWhiteSpace(item.Explanation)) errors.Add(new("SCHEMA_INVALID", index, "explanation"));
        else if (SentenceCount(item.Explanation) > 2) errors.Add(new("LENGTH_LIMIT", index, "explanation"));
    }

    private static void ValidateCauseEffect(ListeningInteractionDraft item, int index, ListeningRuleContext context,
        List<ListeningValidationError> errors)
    {
        if (!CausePairs.IsValid(item.Options.Select(o => (o.Role, o.CorrectOrder)).ToArray()) || HasDuplicateText(item))
        { errors.Add(new("PAIR_INVALID", index, "options")); return; }
        if (item.Options.Any(o => o.IsMatch)) errors.Add(new("MATCH_RULE_INVALID", index, "options"));
        for (var j = 0; j < item.Options.Count; j++)
        {
            var card = item.Options[j];
            if (CountWords(card.Text) > CauseCardMaxWords) errors.Add(new("LENGTH_LIMIT", index, $"options[{j}].text"));
            if (string.IsNullOrWhiteSpace(card.SourceQuote) || PositionBefore(context.Content, card.SourceQuote.Trim(), item.AnchorOffset) < 0)
                errors.Add(new("QUOTE_NOT_FOUND", index, $"options[{j}].sourceQuote"));
        }
        foreach (var pair in item.Options.GroupBy(o => o.CorrectOrder))
            if (pair.Select(o => o.SourceQuote?.Trim()).Distinct().Count() < 2) errors.Add(new("PAIR_INVALID", index, "options"));
    }

    private static void ValidateSceneTitle(ListeningInteractionDraft item, int index, ListeningRuleContext context,
        List<ListeningValidationError> errors)
    {
        if (item.Options.Count != 3 || HasDuplicateText(item)) errors.Add(new("OPTION_COUNT_INVALID", index, "options"));
        if (item.Options.Count(o => o.IsMatch) != 1) errors.Add(new("MATCH_RULE_INVALID", index, "options"));
        for (var j = 0; j < item.Options.Count; j++)
        {
            if (CountWords(item.Options[j].Text) > SceneTitleMaxWords) errors.Add(new("LENGTH_LIMIT", index, $"options[{j}].text"));
            if (!LearningArtifactGrounding.HasRelevantTerm(item.Options[j].Text, context.GroundingSource))
                errors.Add(new("NOT_GROUNDED", index, $"options[{j}].text"));
        }
        if (string.IsNullOrWhiteSpace(item.Explanation)) errors.Add(new("SCHEMA_INVALID", index, "explanation"));
    }

    private static void ValidateThoughtBubble(ListeningInteractionDraft item, int index, ListeningRuleContext context,
        List<ListeningValidationError> errors)
    {
        var name = item.CharacterName?.Trim();
        if (string.IsNullOrWhiteSpace(name) || !AppearsBefore(context.Content, name, item.AnchorOffset))
            errors.Add(new("NOT_GROUNDED", index, "characterName"));
        else if (item.Prompt is null || !item.Prompt.Contains(name, StringComparison.OrdinalIgnoreCase))
            errors.Add(new("SCHEMA_INVALID", index, "question"));
        var quote = item.SourceQuote?.Trim();
        if (string.IsNullOrWhiteSpace(quote) || PositionBefore(context.Content, quote, item.AnchorOffset) < 0)
            errors.Add(new("QUOTE_NOT_FOUND", index, "sourceQuote"));
        if (item.Options.Count != 3 || HasDuplicateText(item)) errors.Add(new("OPTION_COUNT_INVALID", index, "options"));
        if (item.Options.Count(o => o.IsMatch) != 1) errors.Add(new("MATCH_RULE_INVALID", index, "options"));
        for (var j = 0; j < item.Options.Count; j++)
        {
            if (CountWords(item.Options[j].Text) > ThoughtMaxWords) errors.Add(new("LENGTH_LIMIT", index, $"options[{j}].text"));
            if (item.Options[j].Emotion is not { } emotion || !Emotions.Contains(emotion))
                errors.Add(new("EMOTION_INVALID", index, $"options[{j}].emotion"));
        }
        if (item.Options.Select(o => o.Emotion).Distinct().Count() != item.Options.Count) errors.Add(new("EMOTION_INVALID", index, "options"));
    }
}
