namespace StoryPlatform.Application.Features.ContentGeneration;

public sealed class ContentGenerationOptions
{
    public const string SectionName = "AI:ContentGeneration";
    public int MaxContentRefinementAttempts { get; set; } = 2;
    public int ArtifactMaxAttempts { get; set; } = 2;
    public int JobLeaseMinutes { get; set; } = 4;
}
