using System.Text;
using System.Text.RegularExpressions;

namespace StoryPlatform.Application.Features.ContentGeneration.Quality;

/// <summary>
/// Deterministic readability metrics used by generation and human review.
/// English uses the standard FKGL/FRE formula. Vietnamese uses the versioned
/// VI_READABILITY_V1 heuristic and stores comparable grade/ease values in the
/// existing StoryVersion columns.
/// </summary>
public static partial class ReadabilityCalculator
{
    public static ReadabilityResult Calculate(string? content, string? language)
    {
        var value = content ?? string.Empty;
        var words = WordPattern().Matches(value).Select(match => match.Value).ToArray();
        if (words.Length == 0)
            return new ReadabilityResult(Algorithm(language), 0, 0, 0m, null, null);

        var sentenceCount = Math.Max(1, SentencePattern().Matches(value).Count);
        var averageWords = (decimal)words.Length / sentenceCount;

        return IsVietnamese(language)
            ? Vietnamese(words, sentenceCount, averageWords)
            : English(words, sentenceCount, averageWords);
    }

    public static ReadabilityProfileResult EvaluateForProfile(
        string? content,
        string? language,
        int readingLevel,
        decimal? configuredMinimumEaseScore = null)
    {
        var level = Math.Clamp(readingLevel, 1, 5);
        var metrics = Calculate(content, language);
        var minimumEase = configuredMinimumEaseScore is >= 0m and <= 100m
            ? configuredMinimumEaseScore.Value
            : DefaultMinimumEase(level);
        var maximumGrade = level switch
        {
            1 => 3m,
            2 => 5m,
            3 => 7m,
            4 => 9m,
            _ => 11m
        };
        var maximumAverageWords = level switch
        {
            1 => 10m,
            2 => 14m,
            3 => 18m,
            4 => 22m,
            _ => 26m
        };

        var passed = metrics.Fkgl.HasValue && metrics.Fre.HasValue &&
                     metrics.Fkgl.Value <= maximumGrade &&
                     metrics.Fre.Value >= minimumEase &&
                     metrics.AverageWordsPerSentence <= maximumAverageWords;

        return new ReadabilityProfileResult(metrics, passed, minimumEase, maximumGrade, maximumAverageWords);
    }

    private static ReadabilityResult English(string[] words, int sentenceCount, decimal averageWords)
    {
        var syllables = words.Sum(EnglishSyllables);
        var wordsPerSentence = (double)words.Length / sentenceCount;
        var syllablesPerWord = (double)syllables / words.Length;
        var fkgl = 0.39 * wordsPerSentence + 11.8 * syllablesPerWord - 15.59;
        var fre = 206.835 - 1.015 * wordsPerSentence - 84.6 * syllablesPerWord;
        return new ReadabilityResult(
            "FKGL_FRE_EN_V1", words.Length, sentenceCount, decimal.Round(averageWords, 2),
            Round(fkgl), Round(fre));
    }

    private static ReadabilityResult Vietnamese(string[] words, int sentenceCount, decimal averageWords)
    {
        // Vietnamese is syllable-spaced, so English syllable counting is invalid.
        // V1 uses sentence length plus the proportion of longer syllable tokens and
        // average token length to produce stable grade-equivalent/ease scores.
        var averageCharacters = words.Average(word => word.EnumerateRunes().Count());
        var complexRatio = (double)words.Count(word => word.EnumerateRunes().Count() >= 6) / words.Length;
        var wordsPerSentence = (double)words.Length / sentenceCount;
        var gradeEquivalent = Math.Max(0d,
            0.35 * wordsPerSentence + 4.5 * complexRatio + 0.25 * averageCharacters - 2.5);
        var ease = Math.Clamp(
            100d - 3d * wordsPerSentence - 20d * complexRatio - 2d * Math.Max(0d, averageCharacters - 4d),
            0d,
            100d);

        return new ReadabilityResult(
            "VI_READABILITY_V1", words.Length, sentenceCount, decimal.Round(averageWords, 2),
            Round(gradeEquivalent), Round(ease));
    }

    private static decimal DefaultMinimumEase(int readingLevel) => readingLevel switch
    {
        1 => 65m,
        2 => 55m,
        3 => 45m,
        4 => 35m,
        _ => 25m
    };

    private static string Algorithm(string? language) =>
        IsVietnamese(language) ? "VI_READABILITY_V1" : "FKGL_FRE_EN_V1";

    private static bool IsVietnamese(string? language) =>
        !string.IsNullOrWhiteSpace(language) && language.StartsWith("vi", StringComparison.OrdinalIgnoreCase);

    private static decimal Round(double value) => (decimal)Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static int EnglishSyllables(string word)
    {
        var count = 0;
        var previousVowel = false;
        foreach (var character in word.ToLowerInvariant())
        {
            var vowel = "aeiouy".Contains(character);
            if (vowel && !previousVowel) count++;
            previousVowel = vowel;
        }

        if (word.EndsWith('e') && count > 1) count--;
        return Math.Max(1, count);
    }

    [GeneratedRegex(@"\p{L}+(?:['’-]\p{L}+)?", RegexOptions.CultureInvariant)]
    private static partial Regex WordPattern();

    [GeneratedRegex(@"[.!?]+", RegexOptions.CultureInvariant)]
    private static partial Regex SentencePattern();
}

public sealed record ReadabilityResult(
    string Algorithm,
    int WordCount,
    int SentenceCount,
    decimal AverageWordsPerSentence,
    decimal? Fkgl,
    decimal? Fre);

public sealed record ReadabilityProfileResult(
    ReadabilityResult Metrics,
    bool Passed,
    decimal MinimumEaseScore,
    decimal MaximumGradeLevel,
    decimal MaximumAverageWordsPerSentence);
