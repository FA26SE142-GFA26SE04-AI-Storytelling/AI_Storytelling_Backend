using StoryPlatform.Application.Features.ListeningInteractions.Models;
using StoryPlatform.Contracts.AI.Models;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ListeningInteractions.Services;

public static partial class ListeningInteractionRules
{
    public const int TargetLabelMaxWords = 6;
    public const int StatementMaxWords = 12;
    public const int SentenceMaxWords = 8;

    private static void ValidatePictureSearch(ListeningInteractionDraft item, int index, ListeningRuleContext context, List<ListeningValidationError> errors)
    {
        var label = item.TargetLabel?.Trim();
        if (string.IsNullOrWhiteSpace(label) || CountWords(label) > TargetLabelMaxWords)
        { errors.Add(new("SCHEMA_INVALID", index, "targetLabel")); return; }
        if (!AppearsBefore(context.Content, label, item.AnchorOffset)) errors.Add(new("TARGET_NOT_FOUND", index, "targetLabel"));
        if (item.Prompt is null || !item.Prompt.TrimStart().StartsWith("Tìm", StringComparison.OrdinalIgnoreCase) ||
            !item.Prompt.Contains(label, StringComparison.OrdinalIgnoreCase)) errors.Add(new("SCHEMA_INVALID", index, "question"));
        if (item.Options.Count > 0) errors.Add(new("SCHEMA_INVALID", index, "options"));
        if (item.AnchorBlockId is { } id && context.Blocks.FirstOrDefault(b => b.BlockId == id) is { } block &&
            (item.SourceQuote is null || !block.Text.Contains(item.SourceQuote, StringComparison.Ordinal) ||
             !item.SourceQuote.Contains(label, StringComparison.OrdinalIgnoreCase)))
            errors.Add(new("SCHEMA_INVALID", index, "sourceQuote"));
    }

    private static void ValidateTrueFalse(ListeningInteractionDraft item, int index, ListeningRuleContext context, List<ListeningValidationError> errors)
    {
        if (item.Options.Count != 2 || HasDuplicateText(item)) errors.Add(new("OPTION_COUNT_INVALID", index, "options"));
        if (item.Options.Count(o => o.IsMatch) != 1) errors.Add(new("MATCH_RULE_INVALID", index, "options"));
        for (var j = 0; j < item.Options.Count; j++)
        {
            var value = item.Options[j].Text;
            if (CountWords(value) > StatementMaxWords) errors.Add(new("LENGTH_LIMIT", index, $"options[{j}].text"));
            if (!LearningArtifactGrounding.HasRelevantTerm(value, context.GroundingSource)) errors.Add(new("NOT_GROUNDED", index, $"options[{j}].text"));
            // ImageDescription của câu đúng/sai là tên chi tiết làm bằng chứng trong tranh (tùy chọn, không sinh ảnh).
            // Chỉ giới hạn độ dài; chi tiết không có trong tranh thì không định vị được và chỉ mất phần chỉ vào tranh.
            if (item.Options[j].ImageDescription is { } evidence && CountWords(evidence) > TargetLabelMaxWords)
                errors.Add(new("LENGTH_LIMIT", index, $"options[{j}].evidenceLabel"));
        }
        var truth = item.Options.FirstOrDefault(o => o.IsMatch)?.Text;
        var falsehood = item.Options.FirstOrDefault(o => !o.IsMatch)?.Text;
        if (truth is not null && falsehood is not null && IsPlainNegation(truth, falsehood))
            errors.Add(new("TRUEFALSE_TRIVIAL", index, "options"));
    }

    private static bool IsPlainNegation(string a, string b)
    {
        static string[] Words(string s) => Syllables(s).Where(w => w is not ("không" or "chẳng" or "chưa")).ToArray();
        return Words(a).SequenceEqual(Words(b)) && !Syllables(a).SequenceEqual(Syllables(b));
    }

    private static void ValidateFollowAction(ListeningInteractionDraft item, int index, ListeningRuleContext context, List<ListeningValidationError> errors)
    {
        var items = item.Options.Where(o => o.Role == ListeningOptionRole.Item).ToArray();
        var targets = item.Options.Where(o => o.Role == ListeningOptionRole.Target).ToArray();
        if (targets.Length != 1 || items.Length is < 2 or > 3 || item.Options.Any(o => o.Role == ListeningOptionRole.None) || HasDuplicateText(item))
            errors.Add(new("OPTION_COUNT_INVALID", index, "options"));
        if (items.Count(o => o.IsMatch) != 1 || targets.Any(o => o.IsMatch)) errors.Add(new("MATCH_RULE_INVALID", index, "options"));
        for (var j = 0; j < item.Options.Count; j++)
            if (string.IsNullOrWhiteSpace(item.Options[j].ImageDescription)) errors.Add(new("SCHEMA_INVALID", index, $"options[{j}].imageDescription"));
        if (string.IsNullOrWhiteSpace(item.CharacterName) || !AppearsBefore(context.Content, item.CharacterName, item.AnchorOffset))
            errors.Add(new("NOT_GROUNDED", index, "characterName"));
        if (item.Prompt is null || !item.Prompt.StartsWith("Bé giúp", StringComparison.OrdinalIgnoreCase))
            errors.Add(new("SCHEMA_INVALID", index, "question"));
    }

    private static void ValidateSentenceBuilder(ListeningInteractionDraft item, int index, ListeningRuleContext context, List<ListeningValidationError> errors)
    {
        var cards = item.Options.OrderBy(o => o.CorrectOrder).ToArray();
        if (cards.Length is < 3 or > 5 || !cards.Select(o => o.CorrectOrder).SequenceEqual(Enumerable.Range(1, cards.Length).Select(i => (int?)i)) || HasDuplicateText(item))
        { errors.Add(new("OPTION_COUNT_INVALID", index, "options")); return; }
        var quote = item.SourceQuote;
        if (string.IsNullOrWhiteSpace(quote)) { errors.Add(new("SCHEMA_INVALID", index, "sentenceQuote")); return; }
        if (CountWords(quote) > SentenceMaxWords) errors.Add(new("LENGTH_LIMIT", index, "sentenceQuote"));
        if (!AppearsBefore(context.Content, quote, item.AnchorOffset)) errors.Add(new("QUOTE_NOT_FOUND", index, "sentenceQuote"));
        var spelled = string.Join(' ', cards.Select(c => c.Text.Trim()));
        if (!string.Equals(Normalize(StripPunctuation(spelled)), Normalize(StripPunctuation(quote)), StringComparison.OrdinalIgnoreCase))
            errors.Add(new("SCHEMA_INVALID", index, "options"));
        var orders = SentenceOrders.Parse(item.AcceptedOrdersJson);
        if (orders is null || !SentenceOrders.IsValid(orders, cards.Length) || !orders[0].SequenceEqual(Enumerable.Range(1, cards.Length)))
            errors.Add(new("SENTENCE_ORDERS_INVALID", index, "acceptedOrders"));
    }

    private static string StripPunctuation(string value) => new(value.Where(c => !char.IsPunctuation(c)).ToArray());

    private static void ValidateEventOrderingAge7(ListeningInteractionDraft item, int index, List<ListeningValidationError> errors)
    {
        if (item.AnchorOffset is { } anchor && item.Options.Any(o => o.SourceEnd is { } end && end > anchor))
            errors.Add(new("EVENT_AFTER_ANCHOR", index, "events"));
    }
}
