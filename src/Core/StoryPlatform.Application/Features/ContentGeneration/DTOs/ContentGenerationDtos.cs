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
    public string OutputMode { get; init; } = "learning";
    public string StoryStatus { get; init; } = string.Empty;
    public string CurrentStep { get; init; } = "not_started";
    public string Content { get; init; } = "not_started";
    public string Vocabulary { get; init; } = "not_started";
    public string Quiz { get; init; } = "not_started";
    public string Listening { get; init; } = "not_started";
    public bool IsComplete { get; init; }
    public bool WaitingForModeration { get; init; }
    public string? LastErrorCode { get; init; }
    public ContentQualityFailureDto? QualityFailure { get; init; }
    public int? StableStoryVersionId { get; init; }
    public int VocabularyTarget { get; init; }
    public int QuizTarget { get; init; }
    public int VocabularyCount { get; init; }
    public int QuizCount { get; init; }
    public int TotalLearningBatches { get; init; }
    public int CompletedLearningBatches { get; init; }
    public IReadOnlyList<string> LearningWarnings { get; init; } = [];
}
