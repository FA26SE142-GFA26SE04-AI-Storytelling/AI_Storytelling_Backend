namespace StoryPlatform.Contracts.AI.Models;

public sealed record GenerationMetadataDto
{
    public string ModelProvider { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public string ModelVersion { get; init; } = string.Empty;
    public string PromptVersion { get; init; } = string.Empty;
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }
    public long LatencyMs { get; init; }
    public int RefinementCount { get; init; }
    public IReadOnlyList<string> PromptVersions { get; init; } = [];
    public IReadOnlyList<GenerationAttemptMetadataDto> Attempts { get; init; } = [];
}

public sealed record GenerationAttemptMetadataDto
{
    public string Operation { get; init; } = string.Empty;
    public string ModelProvider { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }
    public long LatencyMs { get; init; }
    public string Outcome { get; init; } = "completed";
}

public sealed record EvaluationResultDto
{
    public bool SchemaValid { get; init; }
    public bool SafetyPassed { get; init; }
    public bool ReadabilityPassed { get; init; }
    public bool VocabularyPassed { get; init; }
    public bool QuizPassed { get; init; } = true;
    public bool DiscussionPassed { get; init; } = true;
    public IReadOnlyList<string> Issues { get; init; } = [];
    public IReadOnlyList<EvaluationIssueDto> IssueDetails { get; init; } = [];
    public ReadabilityMetricsDto? ReadabilityMetrics { get; init; }
    public double? SafetyScore { get; init; }
    public string Verdict => SafetyPassed
        ? Passed ? "passed" : "review_required"
        : "blocked";
    public string? FallbackMessage { get; init; }

    public bool Passed => SchemaValid && SafetyPassed && ReadabilityPassed && VocabularyPassed && QuizPassed && DiscussionPassed;
}

public sealed record EvaluationIssueDto
{
    public string Code { get; init; } = string.Empty;
    public string Stage { get; init; } = string.Empty;
    public string Severity { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public bool CanRefine { get; init; }
}
