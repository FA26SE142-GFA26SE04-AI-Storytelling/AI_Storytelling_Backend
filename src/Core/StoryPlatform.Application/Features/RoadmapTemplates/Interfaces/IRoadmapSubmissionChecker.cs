using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Application.Features.RoadmapTemplates.Interfaces;

public sealed record RoadmapCheck(string Code, bool Passed, string Message, int? RelatedRoadmapId = null);

public static class RoadmapCheckCodes
{
    public const string NotEmpty = "not_empty";
    public const string StoriesReady = "stories_ready";
    public const string AgeMatch = "age_match";
    public const string LevelInRange = "level_in_range";
    public const string NoDuplicateStories = "no_duplicate_stories";
    public const string ComboUnique = "combo_unique";
}

public interface IRoadmapSubmissionChecker
{
    Task<List<RoadmapCheck>> EvaluateAsync(
        ReadingRoadmap roadmap,
        IReadOnlyList<ReadingRoadmapItem> items,
        CancellationToken cancellationToken = default);
}
