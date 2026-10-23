using System.Text.Json;
using System.Text.RegularExpressions;

namespace StoryPlatform.Contracts.AI.Models;

public static partial class LearningArtifactGrounding
{
    public static string Canonical(StoryContentDto story) => string.Join("\n\n", story.StorySections.OrderBy(x => x.Order)
        .Select(x => string.IsNullOrWhiteSpace(x.Heading) ? x.Content.Trim() : $"{x.Heading.Trim()}\n{x.Content.Trim()}"));
    public static string Key(string value) => Punctuation().Replace(value.Normalize().ToLowerInvariant(), " ").Trim();
    [GeneratedRegex(@"[^\p{L}\p{N}]+", RegexOptions.CultureInvariant)] private static partial Regex Punctuation();
    public static bool ContainsTerm(string source, string term) => !string.IsNullOrWhiteSpace(term) &&
        Regex.IsMatch(source, @"(?<![\p{L}\p{N}])" + Regex.Escape(term.Trim()) + @"(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static (int Start, int End) Anchor(string source, string quote)
    {
        if (string.IsNullOrWhiteSpace(quote) || quote.Length > 4000) throw new JsonException("LEARNING_EVIDENCE_REQUIRED");
        var start = source.IndexOf(quote, StringComparison.Ordinal);
        if (start < 0) throw new JsonException("LEARNING_EVIDENCE_NOT_IN_STORY");
        return (start, start + quote.Length);
    }

    public static void Vocabulary(StoryContentDto story, IReadOnlyList<GeneratedVocabularyItemDto> items, LearningBatchSpec spec, string reason)
    {
        ValidateCount(items.Count, spec, reason);
        var source = Canonical(story);
        var seen = new HashSet<string>();
        foreach (var item in items)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.Term) || !seen.Add(Key(item.Term)) || item.Term.Length is < 1 or > 150 || string.IsNullOrWhiteSpace(item.Definition) ||
                !ContainsTerm(source, item.Term)) throw new JsonException("VOCABULARY_NOT_GROUNDED_OR_DUPLICATE");
            Anchor(source, item.SourceQuote);
            if (!ContainsTerm(item.SourceQuote, item.Term)) throw new JsonException("VOCABULARY_EVIDENCE_MISMATCH");
            if (!string.IsNullOrEmpty(item.Example)) Anchor(source, item.Example);
        }
    }

    public static void Quiz(StoryContentDto story, IReadOnlyList<QuizItemDto> items, LearningBatchSpec spec, string reason,
        IReadOnlyList<GeneratedVocabularyItemDto>? vocabulary = null)
    {
        ValidateCount(items.Count, spec, reason);
        var source = Canonical(story);
        var seen = spec.ExcludedQuestions.Select(Key).ToHashSet();
        foreach (var item in items)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.Question) || !seen.Add(Key(item.Question)) || string.IsNullOrWhiteSpace(item.Explanation) ||
                string.IsNullOrWhiteSpace(item.Hint) || item.LearningGoal is not ("recall" or "sequence" or "cause" or "inference" or "vocabulary"))
                throw new JsonException("QUIZ_GROUNDING_FIELDS_INVALID");
            Anchor(source, item.SourceQuote);
            if (spec.Kind == "quiz_vocabulary" && (item.LearningGoal != "vocabulary" || vocabulary is null ||
                !vocabulary.Any(v => ContainsTerm(item.Question + " " + item.SourceQuote, v.Term))))
                throw new JsonException("QUIZ_VOCABULARY_DEPENDENCY_INVALID");
            if (item.Type == "multiple_choice")
            {
                if (item.Options is null || item.OptionFeedback is null || item.Options.Any(string.IsNullOrWhiteSpace) ||
                    item.Options.Count is < 2 or > 6 || item.CorrectOptionIndex < 0 || item.CorrectOptionIndex >= item.Options.Count ||
                    item.Options.Distinct(StringComparer.OrdinalIgnoreCase).Count() != item.Options.Count ||
                    item.OptionFeedback.Count != item.Options.Count || item.OptionFeedback.Any(string.IsNullOrWhiteSpace) ||
                    !string.Equals(item.CorrectAnswer, item.Options[item.CorrectOptionIndex], StringComparison.OrdinalIgnoreCase))
                    throw new JsonException("QUIZ_OPTIONS_INVALID");
            }
            else if (item.Type == "true_false")
            { if (!bool.TryParse(item.CorrectAnswer, out _)) throw new JsonException("QUIZ_TRUE_FALSE_INVALID"); }
            else if (item.Type != "short_answer" || string.IsNullOrWhiteSpace(item.CorrectAnswer))
                throw new JsonException("QUIZ_ANSWER_INVALID");
        }
    }

    private static void ValidateCount(int count, LearningBatchSpec spec, string reason)
    {
        if (spec.TargetCount is < 1 or > 40 || count > spec.TargetCount || count < spec.TargetCount && string.IsNullOrWhiteSpace(reason))
            throw new JsonException("LEARNING_BATCH_COUNT_INVALID");
        if (reason?.Length > 300) throw new JsonException("LEARNING_SHORTFALL_REASON_TOO_LONG");
    }
}
