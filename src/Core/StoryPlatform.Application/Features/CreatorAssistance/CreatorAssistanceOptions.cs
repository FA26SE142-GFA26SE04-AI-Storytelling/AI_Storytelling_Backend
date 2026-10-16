namespace StoryPlatform.Application.Features.CreatorAssistance;

public sealed class CreatorAssistanceOptions
{
    public const int CompletionTimeoutSeconds = 10;
    public const int LeaseSafetyMarginSeconds = 5;
    public const string SectionName = "CreatorAssistance";
    public int MaximumSnapshotCharacters { get; set; } = 30_000;
    public int MaximumTrialCharacters { get; set; } = 3000;
    // The deadline includes structured generation followed by a separate safety assessment.
    public int TimeoutSeconds { get; set; } = 120;
    public int LeaseSeconds { get; set; } = 150;
    public int MaximumRequestsPerMinute { get; set; } = 10;

    public bool HasValidLimits() => MaximumSnapshotCharacters is > 0 and <= 200_000 &&
        MaximumTrialCharacters is > 0 and <= 10_000 && TimeoutSeconds is >= 5 and <= 150 &&
        LeaseSeconds >= TimeoutSeconds + CompletionTimeoutSeconds + LeaseSafetyMarginSeconds && LeaseSeconds <= 300 &&
        MaximumRequestsPerMinute is > 0 and <= 100;
}
