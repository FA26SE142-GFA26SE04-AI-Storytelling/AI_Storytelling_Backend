using System.Text.RegularExpressions;

namespace StoryPlatform.Contracts.AI.Models;

public sealed record LearningGenerationProfile(int TargetWords, int MinimumWords, int MaximumWords,
    int VocabularyTarget, int QuizTarget)
{
    public static LearningGenerationProfile ForAge(string ageBand, int? requestedWords = null)
    {
        var older = ageBand is "9-10" or "Age_9_10";
        if (!older && ageBand is not ("6-8" or "Age_6_8")) throw new ArgumentException("Unsupported age band.", nameof(ageBand));
        var target = requestedWords is > 0 ? requestedWords.Value : older ? 1500 : 900;
        return new(target, (int)Math.Ceiling(target * .9), (int)Math.Floor(target * 1.1), older ? 18 : 12, older ? 36 : 24);
    }
}

public sealed record LearningBatchSpec
{
    public static IReadOnlyList<string> QuizTypesForBatch(string kind, int count) => count >= 3
        ? ["multiple_choice", "true_false", "short_answer"]
        : kind switch
        {
            "quiz_facts" => ["multiple_choice"],
            "quiz_reasoning" => ["short_answer"],
            "quiz_vocabulary" => ["true_false"],
            _ => []
        };
    public string Kind { get; init; } = "vocabulary";
    public int TargetCount { get; init; }
    public int BatchNumber { get; init; }
    public IReadOnlyList<string> ExcludedQuestions { get; init; } = [];
    public IReadOnlyList<string> RequiredQuestionTypes { get; init; } = [];
    public bool RequireEvidence { get; init; } = true;
    public int TechnicalAttempts { get; init; } = 1;
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? ValidationErrorCode { get; init; }
}

/// <summary>Same Unicode lexical-unit count on Core and AI; not linguistic Vietnamese segmentation.</summary>
public static partial class StoryWordCounter
{
    public static int Count(string text) => Words().Matches(text ?? string.Empty).Count;
    [GeneratedRegex(@"[\p{L}\p{N}]+(?:['’-][\p{L}\p{N}]+)*", RegexOptions.CultureInvariant)]
    private static partial Regex Words();
}
