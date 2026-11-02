namespace StoryPlatform.Contracts.AI.Models;

/// <summary>Effective thresholds shared by the writer prompt and the Core quality gate.</summary>
public sealed record ContentReadabilityLimits(decimal MaximumGradeLevel, decimal MinimumEaseScore,
    decimal MaximumAverageWordsPerSentence)
{
    public int SentenceTarget(string? language)
    {
        var target = MaximumAverageWordsPerSentence * .7m;
        if (language?.StartsWith("vi", StringComparison.OrdinalIgnoreCase) == true)
        {
            // VI_READABILITY_V1: ease = 100 - 3 * sentence length - lexical penalties.
            // Reserve four ease points for lexical complexity; this is guidance, not a pass guarantee.
            target = Math.Min(target, (100m - MinimumEaseScore - 4m) / 3m);
        }
        return Math.Max(1, (int)Math.Floor(target));
    }

    public static ContentReadabilityLimits ForLevel(int readingLevel, decimal? configuredMinimumEase = null)
    {
        var level = Math.Clamp(readingLevel, 1, 5);
        var ease = configuredMinimumEase is >= 0m and <= 100m
            ? configuredMinimumEase.Value : 75m - 10m * level;
        return new(1m + 2m * level, ease, 6m + 4m * level);
    }
}

public sealed record ContentRefinementFeedback
{
    public ContentReadabilityFeedback? Readability { get; init; }
    public int CurrentWordCount { get; init; }
    public int TargetWords { get; init; }
    public int MinimumWords { get; init; }
    public int MaximumWords { get; init; }
    public IReadOnlyList<string> FailedGateCodes { get; init; } = [];
}

public sealed record ContentReadabilityFeedback(string Algorithm, decimal? Grade, decimal? Ease,
    decimal AverageWordsPerSentence, ContentReadabilityLimits Limits, int SentenceTarget,
    IReadOnlyList<string> FailedMetrics);
