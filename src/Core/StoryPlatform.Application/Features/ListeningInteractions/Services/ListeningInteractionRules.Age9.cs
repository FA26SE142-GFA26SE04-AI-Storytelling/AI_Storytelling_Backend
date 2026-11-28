using StoryPlatform.Application.Features.ListeningInteractions.Models;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ListeningInteractions.Services;

public static partial class ListeningInteractionRules
{
    private static readonly HashSet<string> RelationLabels =
        ["giúp đỡ", "cản trở", "bảo vệ", "tin tưởng", "hiểu lầm"];

    // "tưởng" phải đứng riêng, không phải trong "tin tưởng".
    private static readonly System.Text.RegularExpressions.Regex MisunderstandingWords = new(
        @"(?<![\p{L}])(nhầm|hiểu lầm|(?<!tin )tưởng)(?![\p{L}])",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static void ValidateCharacterTrial(ListeningInteractionDraft item, int index,
        ListeningRuleContext context, List<ListeningValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(item.Prompt) || SentenceSplitter.Split(item.Prompt).Count is < 1 or > 2 ||
            CountWords(item.Prompt) > 40)
            errors.Add(new("LENGTH_LIMIT", index, "situation"));
        RequireEarlierQuote(item.SourceQuote, item.AnchorOffset, context.Content, index, "decisionQuote", errors);
        if (item.Options.Count is < 3 or > 4 || HasDuplicateText(item))
            errors.Add(new("OPTION_COUNT_INVALID", index, "reasons"));
        if (item.Options.Any(option => option.IsMatch)) errors.Add(new("MATCH_RULE_INVALID", index, "reasons"));
        for (var j = 0; j < item.Options.Count; j++)
        {
            var option = item.Options[j];
            if (CountWords(option.Text) > 12) errors.Add(new("LENGTH_LIMIT", index, $"reasons[{j}].text"));
            if (string.IsNullOrWhiteSpace(option.Feedback) || string.IsNullOrWhiteSpace(option.Counterpoint))
                errors.Add(new("TWO_SIDES_MISSING", index, $"reasons[{j}]"));
        }
    }

    private static void ValidateFindWrongDetail(ListeningInteractionDraft item, int index,
        ListeningRuleContext context, List<ListeningValidationError> errors)
    {
        var original = item.SourceQuote?.Trim();
        RequireEarlierQuote(original, item.AnchorOffset, context.Content, index, "originalQuote", errors);
        if (original is null || SentenceSplitter.Split(original).Count is < 2 or > 4 || CountWords(original) > 60)
            errors.Add(new("LENGTH_LIMIT", index, "originalQuote"));
        if (item.Options.Count != 3 || HasDuplicateText(item))
            errors.Add(new("OPTION_COUNT_INVALID", index, "choices"));
        if (item.Options.Count(option => option.IsMatch) != 1)
            errors.Add(new("MATCH_RULE_INVALID", index, "choices"));
        for (var j = 0; j < item.Options.Count; j++)
            if (CountWords(item.Options[j].Text) > 6)
                errors.Add(new("LENGTH_LIMIT", index, $"choices[{j}].text"));
        var correct = item.Options.FirstOrDefault(option => option.IsMatch)?.Text.Trim();
        var wrong = item.HiddenWord?.Trim();
        if (string.IsNullOrWhiteSpace(item.Prompt) || string.IsNullOrWhiteSpace(wrong) ||
            string.IsNullOrWhiteSpace(correct) || original is null ||
            item.Prompt.Split(wrong, StringSplitOptions.None).Length != 2 ||
            !string.Equals(item.Prompt.Replace(wrong, correct, StringComparison.Ordinal), original, StringComparison.Ordinal))
            errors.Add(new("DETAIL_SWAP_INVALID", index, "retold"));
        if (string.IsNullOrWhiteSpace(item.Explanation)) errors.Add(new("SCHEMA_INVALID", index, "explanation"));
    }

    private static void ValidateRelationshipMap(ListeningInteractionDraft item, int index,
        ListeningRuleContext context, List<ListeningValidationError> errors)
    {
        var nodes = item.Options.Where(option => option.Role == ListeningOptionRole.Node).ToArray();
        var cards = item.Options.Where(option => option.Role == ListeningOptionRole.Card).ToArray();
        var map = RelationMap.TryParse(item.RelationsJson);
        if (item.Options.Count != nodes.Length + cards.Length || nodes.Length is < 3 or > 4 ||
            cards.Length is < 4 or > 5 || map is null || !map.IsValid(nodes.Length, cards.Length) ||
            nodes.Select(node => Normalize(node.Text)).Distinct().Count() != nodes.Length ||
            cards.Select(card => Normalize(card.Text)).Distinct().Count() != cards.Length ||
            cards.Any(card => !RelationLabels.Contains(Normalize(card.Text))))
        {
            errors.Add(new("RELATION_MAP_INVALID", index, "relations"));
            return;
        }
        for (var nodeIndex = 0; nodeIndex < nodes.Length; nodeIndex++)
            if (string.IsNullOrWhiteSpace(nodes[nodeIndex].ImageDescription) ||
                !AppearsBefore(context.Content, nodes[nodeIndex].Text, item.AnchorOffset))
                errors.Add(new("RELATION_MAP_INVALID", index, $"characters[{nodeIndex}]"));
        for (var edgeIndex = 0; edgeIndex < map.Edges.Count; edgeIndex++)
        {
            var edge = map.Edges[edgeIndex];
            var grounded = PositionBefore(context.Content, edge.Evidence, item.AnchorOffset) >= 0 &&
                edge.Evidence.Contains(nodes[edge.From].Text, StringComparison.OrdinalIgnoreCase) &&
                edge.Evidence.Contains(nodes[edge.To].Text, StringComparison.OrdinalIgnoreCase);
            var misunderstanding = edge.Accepted.Any(cardIndex => Normalize(cards[cardIndex].Text) == "hiểu lầm");
            if (!grounded || misunderstanding &&
                !MisunderstandingWords.IsMatch(edge.Evidence) ||
                !context.IsSafe(edge.Explanation))
                errors.Add(new("RELATION_EVIDENCE_INVALID", index, $"relations[{edgeIndex}]"));
        }
    }

    private static void ValidateStoryEditor(ListeningInteractionDraft item, int index,
        ListeningRuleContext context, List<ListeningValidationError> errors)
    {
        var passage = item.SourceQuote?.Trim();
        RequireEarlierQuote(passage, item.AnchorOffset, context.Content, index, "passage", errors);
        var sentences = SentenceSplitter.Split(passage ?? string.Empty);
        if (sentences.Count is < 5 or > 6 || item.Options.Count != sentences.Count ||
            !item.Options.Select(option => option.CorrectOrder).SequenceEqual(
                Enumerable.Range(1, item.Options.Count).Select(order => (int?)order)) ||
            !item.Options.Select(option => option.Text).SequenceEqual(sentences))
            errors.Add(new("EDITOR_SENTENCES_INVALID", index, "sentences"));
        if (item.Options.Count(option => option.IsMatch) is < 2 or > 3 ||
            item.Options.Any(option => option.IsMatch && string.IsNullOrWhiteSpace(option.Feedback)))
            errors.Add(new("MAIN_IDEA_COUNT_INVALID", index, "sentences"));
    }

    private static void ValidateWhatIf(ListeningInteractionDraft item, int index,
        ListeningRuleContext context, List<ListeningValidationError> errors)
    {
        RequireEarlierQuote(item.SourceQuote, item.AnchorOffset, context.Content, index, "decisionQuote", errors);
        if (item.Prompt is null || !item.Prompt.TrimStart().StartsWith("Nếu", StringComparison.OrdinalIgnoreCase) ||
            !item.Prompt.Contains("thì", StringComparison.OrdinalIgnoreCase) || !item.Prompt.TrimEnd().EndsWith('?'))
            errors.Add(new("SCHEMA_INVALID", index, "question"));
        if (item.Options.Count != 3 || HasDuplicateText(item)) errors.Add(new("OPTION_COUNT_INVALID", index, "outcomes"));
        if (item.Options.Any(option => option.IsMatch)) errors.Add(new("MATCH_RULE_INVALID", index, "outcomes"));
        for (var j = 0; j < item.Options.Count; j++)
        {
            if (CountWords(item.Options[j].Text) > 12) errors.Add(new("LENGTH_LIMIT", index, $"outcomes[{j}].text"));
            if (string.IsNullOrWhiteSpace(item.Options[j].Feedback))
                errors.Add(new("FEEDBACK_MISSING", index, $"outcomes[{j}].feedback"));
        }
    }

    private static void RequireEarlierQuote(string? quote, int? anchor, string content, int index,
        string field, List<ListeningValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(quote) || PositionBefore(content, quote.Trim(), anchor) < 0)
            errors.Add(new("NOT_GROUNDED", index, field));
    }
}
