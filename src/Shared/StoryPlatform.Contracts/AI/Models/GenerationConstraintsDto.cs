namespace StoryPlatform.Contracts.AI.Models;

public sealed record GenerationConstraintsDto
{
    public int MaximumWords { get; init; } = 1200;
    public IReadOnlyList<string> BlockedTopics { get; init; } = [];
    public IReadOnlyList<string> RestrictedTopics { get; init; } = [];
    public IReadOnlyList<string> AllowedTopics { get; init; } = [];
}
