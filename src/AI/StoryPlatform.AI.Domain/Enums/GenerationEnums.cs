namespace StoryPlatform.AI.Domain.Enums;

public enum GenerationOperation
{
    OutlineGeneration = 1,
    StoryExpansion = 2,
    Refinement = 3,
    Evaluation = 4
}

public enum GenerationStatus
{
    Pending = 1,
    Running = 2,
    Completed = 3,
    Failed = 4
}

public enum PromptType
{
    Outline = 1,
    Story = 2,
    Refinement = 3
}
