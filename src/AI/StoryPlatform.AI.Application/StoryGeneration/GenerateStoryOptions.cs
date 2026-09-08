namespace StoryPlatform.AI.Application.StoryGeneration;

public sealed class GenerateStoryOptions
{
    public const string SectionName = "AI:Generation";
    public int MaxRefinementAttempts { get; set; } = 2;
}
