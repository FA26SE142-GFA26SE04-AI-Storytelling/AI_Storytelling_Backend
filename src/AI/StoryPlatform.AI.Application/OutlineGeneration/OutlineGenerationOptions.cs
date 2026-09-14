namespace StoryPlatform.AI.Application.OutlineGeneration;

public sealed class OutlineGenerationOptions
{
    public const string SectionName = "AI:OutlineGeneration";

    public int MaxAttempts { get; set; } = 3;
    public int TimeoutSeconds { get; set; } = 60;
    public int BaseRetryDelaySeconds { get; set; } = 2;
    public int MaximumTitleLength { get; set; } = 200;
    public int MaximumSectionLength { get; set; } = 4_000;
}
