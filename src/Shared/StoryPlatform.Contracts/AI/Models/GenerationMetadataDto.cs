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
}

public sealed record EvaluationResultDto
{
    public bool SchemaValid { get; init; }
    public bool SafetyPassed { get; init; }
    public bool ReadabilityPassed { get; init; }
    public bool VocabularyPassed { get; init; }
    public IReadOnlyList<string> Issues { get; init; } = [];

    public bool Passed => SchemaValid && SafetyPassed && ReadabilityPassed && VocabularyPassed;
}
