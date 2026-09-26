namespace StoryPlatform.Contracts.AI.Models;

/// <summary>Immutable-at-job-creation settings and prompt text supplied by Core to AI.</summary>
public sealed record AiGenerationSnapshot
{
    public int PromptCatalogVersionId { get; init; }
    public string PromptVersionNo { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string> Templates { get; init; } = new Dictionary<string, string>();
    public AiGenerationConfigSnapshot Config { get; init; } = new();
}

public sealed record AiGenerationConfigSnapshot
{
    public int MaxOutlineAttempts { get; init; } = 3;
    public int OutlineTimeoutSeconds { get; init; } = 60;
    public int MaxTitleLength { get; init; } = 200;
    public int MaxSectionLength { get; init; } = 4_000;
    public int MaxRefinementAttempts { get; init; } = 2;
    public int MaxVocabularyItems { get; init; } = 10;
    public int MaxQuizItems { get; init; } = 5;
    public int MinStorySections { get; init; } = 3;
    public int MaxStorySections { get; init; } = 10;
}
