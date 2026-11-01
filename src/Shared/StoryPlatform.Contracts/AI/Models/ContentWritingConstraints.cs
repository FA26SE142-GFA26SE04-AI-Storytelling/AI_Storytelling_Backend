namespace StoryPlatform.Contracts.AI.Models;

/// <summary>Effective thresholds shared by the writer prompt and the Core quality gate.</summary>
public sealed record ContentReadabilityLimits(decimal MaximumGradeLevel, decimal MinimumEaseScore,
    decimal MaximumAverageWordsPerSentence)
{
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
    public int CurrentWordCount { get; init; }
    public int TargetWords { get; init; }
    public int MinimumWords { get; init; }
    public int MaximumWords { get; init; }
    public IReadOnlyList<string> FailedGateCodes { get; init; } = [];
}
