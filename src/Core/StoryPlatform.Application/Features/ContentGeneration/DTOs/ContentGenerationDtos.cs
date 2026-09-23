using System.ComponentModel.DataAnnotations;

namespace StoryPlatform.Application.Features.ContentGeneration.DTOs;

public sealed class RetryContentGenerationRequestDto
{
    [Required, StringLength(100, MinimumLength = 8)]
    public string RetryKey { get; init; } = string.Empty;
}

public sealed class ContentQualityGateFailureDto
{
    public string Gate { get; init; } = string.Empty;
    public string ReasonCode { get; init; } = string.Empty;
    public IReadOnlyList<string> Violations { get; init; } = [];
}

public sealed class ContentQualityFailureDto
{
    public int RefinementAttempts { get; init; }
    public decimal? SafetyScore { get; init; }
    public IReadOnlyList<ContentQualityGateFailureDto> FailedGates { get; init; } = [];
}

public sealed class ContentGenerationProgressDto
{
    public int StoryId { get; init; }
    public string StoryStatus { get; init; } = string.Empty;
    public string CurrentStep { get; init; } = "not_started";
    public string Content { get; init; } = "not_started";
    public string Vocabulary { get; init; } = "not_started";
    public string Quiz { get; init; } = "not_started";
    public string Discussion { get; init; } = "not_started";
    public bool IsComplete { get; init; }
    public string? LastErrorCode { get; init; }
    public ContentQualityFailureDto? QualityFailure { get; init; }
    public int? StableStoryVersionId { get; init; }
}
