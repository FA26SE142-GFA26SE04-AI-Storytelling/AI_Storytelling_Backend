namespace StoryPlatform.Application.Features.ContentGeneration.DTOs;

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
    public int? StableStoryVersionId { get; init; }
}
