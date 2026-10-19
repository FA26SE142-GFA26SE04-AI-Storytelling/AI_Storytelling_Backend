using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.RoadmapTemplates;

internal static class RoadmapNeedsUpdate
{
    public static bool IsRetired(Story? story) =>
        story == null
        || story.IsDeleted
        || story.Status == StoryStatus.Archived
        || story.ArchivedAt != null;

    public static async Task<Dictionary<int, List<int>>> RetiredStoriesByRoadmapAsync(
        IUnitOfWork unitOfWork,
        IReadOnlyCollection<ReadingRoadmapItem> items,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<int, List<int>>();
        if (items.Count == 0)
        {
            return result;
        }

        var storyIds = items.Select(item => item.StoryId).Distinct().ToList();
        var stories = await unitOfWork.Repository<Story>().FindAsync(
            story => storyIds.Contains(story.Id), cancellationToken: cancellationToken);
        var byId = stories.ToDictionary(story => story.Id);

        foreach (var item in items
                     .OrderBy(candidate => candidate.ReadingRoadmapId)
                     .ThenBy(candidate => candidate.Position))
        {
            byId.TryGetValue(item.StoryId, out var story);
            if (!IsRetired(story))
            {
                continue;
            }

            if (!result.TryGetValue(item.ReadingRoadmapId, out var retired))
            {
                retired = new List<int>();
                result[item.ReadingRoadmapId] = retired;
            }

            retired.Add(item.StoryId);
        }

        return result;
    }
}
