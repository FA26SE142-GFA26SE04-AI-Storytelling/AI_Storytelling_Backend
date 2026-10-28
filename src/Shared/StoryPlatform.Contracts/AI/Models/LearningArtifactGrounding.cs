using System.Globalization;
using System.Text;
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

    /// <summary>
    /// Locates <paramref name="quote"/> in <paramref name="source"/> and returns the range of the
    /// <em>original</em> text it covers. An exact ordinal match always wins; otherwise the two strings are
    /// compared through <see cref="Fold"/> so that a model that reflowed a line break, swapped a curly quote
    /// for a straight one, collapsed a double space or emitted decomposed Vietnamese diacritics still lands on
    /// the verbatim passage. Because the range is mapped back to the original string, callers can persist
    /// <c>source[Start..End]</c> as the canonical quotation.
    /// </summary>
    public static (int Start, int End) Anchor(string source, string quote)
    {
        if (!TryAnchor(source, quote, out var range)) throw new JsonException(NotInStoryReason(source, quote));
        return range;
    }

    /// <summary>Distinguishes an unusable quote (empty/oversized) from a quote that is genuinely absent.</summary>
    private static string NotInStoryReason(string? source, string? quote) =>
        string.IsNullOrWhiteSpace(quote) || quote.Length > MaxQuoteLength || string.IsNullOrEmpty(source)
            ? "LEARNING_EVIDENCE_REQUIRED"
            : "LEARNING_EVIDENCE_NOT_IN_STORY";

    public static bool TryAnchor(string? source, string? quote, out (int Start, int End) Range)
    {
        Range = default;
        if (string.IsNullOrWhiteSpace(quote) || quote.Length > MaxQuoteLength || string.IsNullOrEmpty(source)) return false;
        if (TryAnchorExact(source, quote, out Range)) return true;
        // Models often wrap the evidence in quotation marks of their own; those characters are not part of the
        // story, so drop the wrapper and retry rather than failing the whole batch.
        var unwrapped = quote.Trim().Trim(QuoteWrappers.ToCharArray());
        return unwrapped.Length != quote.Trim().Length && TryAnchorExact(source, unwrapped, out Range);
    }

    private const string QuoteWrappers = "\"'`\u00AB\u00BB\u2018\u2019\u201A\u201B\u201C\u201D\u201E\u201F\u2032\u2033";

    private static bool TryAnchorExact(string source, string quote, out (int Start, int End) Range)
    {
        Range = default;
        var trimmed = quote.Trim();
        if (trimmed.Length == 0) return false;
        var exact = source.IndexOf(trimmed, StringComparison.Ordinal);
        if (exact >= 0) { Range = (exact, exact + trimmed.Length); return true; }
        var target = Fold(trimmed).Text;
        if (target.Length == 0) return false;
        var folded = Fold(source);
        var found = folded.Text.IndexOf(target, StringComparison.Ordinal);
        if (found < 0) return false;
        Range = (folded.Starts[found], folded.Ends[found + target.Length - 1]);
        return true;
    }

    private const int MaxQuoteLength = 4000;

    public static IReadOnlyList<GeneratedVocabularyItemDto> Vocabulary(StoryContentDto story,
        IReadOnlyList<GeneratedVocabularyItemDto> items, LearningBatchSpec spec, string reason)
    {
        ValidateCount(items.Count, spec, reason);
        var source = Canonical(story);
        var seen = new HashSet<string>();
        var repaired = new List<GeneratedVocabularyItemDto>(items.Count);
        foreach (var item in items)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.Term) || !seen.Add(Key(item.Term)) || item.Term.Length is < 1 or > 150 || string.IsNullOrWhiteSpace(item.Definition) ||
                !ContainsTerm(source, item.Term)) throw new JsonException("VOCABULARY_NOT_GROUNDED_OR_DUPLICATE");
            var quote = Repair(source, item.SourceQuote, "LEARNING_EVIDENCE_NOT_IN_STORY");
            if (!ContainsTerm(quote, item.Term)) throw new JsonException("VOCABULARY_EVIDENCE_MISMATCH");
            var example = string.IsNullOrEmpty(item.Example) ? item.Example
                : Repair(source, item.Example, "LEARNING_EVIDENCE_NOT_IN_STORY");
            repaired.Add(item with { SourceQuote = quote, Example = example });
        }
        return repaired;
    }

    public static IReadOnlyList<QuizItemDto> Quiz(StoryContentDto story, IReadOnlyList<QuizItemDto> items, LearningBatchSpec spec, string reason,
        IReadOnlyList<GeneratedVocabularyItemDto>? vocabulary = null)
    {
        ValidateCount(items.Count, spec, reason);
        var source = Canonical(story);
        var seen = spec.ExcludedQuestions.Select(Key).ToHashSet();
        var repaired = new List<QuizItemDto>(items.Count);
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            if (item is null) throw ItemFailure("QUIZ_ITEM_REQUIRED", index);
            if (string.IsNullOrWhiteSpace(item.Question)) throw ItemFailure("QUIZ_QUESTION_REQUIRED", index);
            if (spec.RequiredQuestionTypes.Count > 0 && !HasRelevantTerm(item.Question, source))
                throw ItemFailure("QUIZ_QUESTION_RELEVANCE_INVALID", index);
            if (!seen.Add(Key(item.Question))) throw ItemFailure("QUIZ_QUESTION_DUPLICATE", index);
            if (string.IsNullOrWhiteSpace(item.Explanation)) throw ItemFailure("QUIZ_EXPLANATION_REQUIRED", index);
            if (string.IsNullOrWhiteSpace(item.Hint)) throw ItemFailure("QUIZ_HINT_REQUIRED", index);
            if (item.LearningGoal is not ("recall" or "sequence" or "cause" or "inference" or "vocabulary"))
                throw ItemFailure("QUIZ_LEARNING_GOAL_INVALID", index);
            var quote = Repair(source, item.SourceQuote, "LEARNING_EVIDENCE_NOT_IN_STORY", index);
            if (spec.Kind == "quiz_vocabulary" && (item.LearningGoal != "vocabulary" || vocabulary is null ||
                !vocabulary.Any(v => ContainsTerm(item.Question + " " + quote, v.Term))))
                throw ItemFailure("QUIZ_VOCABULARY_DEPENDENCY_INVALID", index);
            if (item.Type == "multiple_choice")
            {
                if (item.Options is null || item.OptionFeedback is null || item.Options.Any(string.IsNullOrWhiteSpace) ||
                    item.Options.Count is < 2 or > 6 || item.CorrectOptionIndex < 0 || item.CorrectOptionIndex >= item.Options.Count ||
                    item.Options.Distinct(StringComparer.OrdinalIgnoreCase).Count() != item.Options.Count ||
                    item.OptionFeedback.Count != item.Options.Count || item.OptionFeedback.Any(string.IsNullOrWhiteSpace) ||
                    !string.Equals(item.CorrectAnswer, item.Options[item.CorrectOptionIndex], StringComparison.OrdinalIgnoreCase))
                    throw ItemFailure("QUIZ_OPTIONS_INVALID", index);
            }
            else if (item.Type == "true_false")
            { if (!bool.TryParse(item.CorrectAnswer, out _)) throw ItemFailure("QUIZ_TRUE_FALSE_INVALID", index); }
            else if (item.Type != "short_answer" || string.IsNullOrWhiteSpace(item.CorrectAnswer))
                throw ItemFailure("QUIZ_ANSWER_INVALID", index);
            repaired.Add(item with { SourceQuote = quote });
        }
        if (items.Count >= spec.RequiredQuestionTypes.Count && spec.RequiredQuestionTypes.Any(type => !items.Any(item => item.Type == type)))
            throw new JsonException("QUIZ_TYPE_COVERAGE_INVALID");
        return repaired;
    }

    public static bool HasRelevantTerm(string value, string source)
    {
        var terms = value.Split([' ', '\t', '\r', '\n', '?', '!', '.', ',', ':', ';'], StringSplitOptions.RemoveEmptyEntries)
            .Where(item => item.Length >= 4).ToArray();
        return terms.Length == 0 || terms.Any(item => source.Contains(item, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Replaces a near-miss quotation with the verbatim passage it was meant to copy, so downstream highlighting
    /// and persistence see exact story text rather than the model's reflowed rendering of it.
    /// </summary>
    private static string Repair(string source, string? quote, string missingCode, int? index = null)
    {
        if (string.IsNullOrWhiteSpace(quote) || quote.Length > MaxQuoteLength) throw Fail(missingCode, index);
        if (source.IndexOf(quote, StringComparison.Ordinal) >= 0) return quote;
        if (TryAnchor(source, quote, out var range)) return source[range.Start..range.End];
        throw Fail(missingCode, index);
    }

    private static JsonException Fail(string code, int? index) => index is int value ? ItemFailure(code, value) : new JsonException(code);

    private static JsonException ItemFailure(string code, int index)
    {
        var exception = new JsonException(code);
        exception.Data["LearningItemIndex"] = index;
        return exception;
    }

    private static void ValidateCount(int count, LearningBatchSpec spec, string reason)
    {
        if (spec.TargetCount is < 1 or > 40 || count > spec.TargetCount || count < spec.TargetCount && string.IsNullOrWhiteSpace(reason))
            throw new JsonException("LEARNING_BATCH_COUNT_INVALID");
        if (reason?.Length > 300) throw new JsonException("LEARNING_SHORTFALL_REASON_TOO_LONG");
    }

    private readonly record struct Folded(string Text, int[] Starts, int[] Ends);

    /// <summary>
    /// Produces a comparison-only projection of <paramref name="source"/> together with the map from every
    /// projected character back to the original range it came from. Diacritics, dash and quote variants,
    /// invisible formatting characters and whitespace runs all collapse; nothing else changes, so unrelated
    /// passages never become equal.
    /// </summary>
    private static Folded Fold(string source)
    {
        var text = new StringBuilder(source.Length);
        var starts = new List<int>(source.Length);
        var ends = new List<int>(source.Length);
        var inWhitespace = false;
        var elements = StringInfo.GetTextElementEnumerator(source);
        while (elements.MoveNext())
        {
            var element = (string)elements.Current;
            var start = elements.ElementIndex;
            var end = start + element.Length;
            // Composing each grapheme collapses decomposed Vietnamese diacritics onto their precomposed form.
            var normalized = element.Normalize(NormalizationForm.FormC);
            foreach (var rune in normalized.EnumerateRunes())
            {
                foreach (var character in Project(rune))
                {
                    if (char.IsWhiteSpace(character))
                    {
                        if (inWhitespace) continue;
                        inWhitespace = true;
                        text.Append(' ');
                        starts.Add(start); ends.Add(end);
                        continue;
                    }
                    inWhitespace = false;
                    text.Append(character);
                    starts.Add(start); ends.Add(end);
                }
            }
        }
        return new(text.ToString(), starts.ToArray(), ends.ToArray());
    }

    private static string Project(Rune rune) => rune.Value switch
    {
        // Non-breaking and thin spaces behave as ordinary spaces for comparison.
        0x00A0 or 0x2007 or 0x202F or 0x205F => " ",
        // Apostrophe family.
        0x0060 or 0x00B4 or 0x2018 or 0x2019 or 0x201A or 0x201B or 0x2032 => "'",
        // Quotation-mark family, including the guillemets.
        0x00AB or 0x00BB or 0x201C or 0x201D or 0x201E or 0x201F or 0x2033 => "\"",
        // Dashes and the minus sign.
        0x2010 or 0x2011 or 0x2012 or 0x2013 or 0x2014 or 0x2015 or 0x2212 => "-",
        0x2026 => "...",
        // Zero-width and formatting characters carry no meaning in an exact quotation.
        0x00AD or 0x200B or 0x200C or 0x200D or 0x2060 or 0xFEFF => string.Empty,
        _ => Rune.ToLowerInvariant(rune).ToString()
    };
}
