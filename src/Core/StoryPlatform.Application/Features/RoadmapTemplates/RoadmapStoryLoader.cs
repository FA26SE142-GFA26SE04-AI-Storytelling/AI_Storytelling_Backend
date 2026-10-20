using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.RoadmapTemplates.DTOs;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Application.Features.RoadmapTemplates;

internal static class RoadmapStoryLoader
{
    public static async Task<List<RoadmapStoryDto>> LoadAsync(
        IUnitOfWork unitOfWork,
        IReadOnlyList<ReadingRoadmapItem> items,
        CancellationToken cancellationToken)
    {
        var storyIds = items.Select(item => item.StoryId).Distinct().ToList();
        var stories = await unitOfWork.Repository<Story>().FindAsync(
            story => storyIds.Contains(story.Id), cancellationToken: cancellationToken);
        var byId = stories.ToDictionary(story => story.Id);

        return items.OrderBy(item => item.Position).Select(item =>
        {
            byId.TryGetValue(item.StoryId, out var story);
            return new RoadmapStoryDto
            {
                StoryId = item.StoryId,
                Position = item.Position,
                Days = item.Days,
                Title = story?.Title,
                AgeBand = story?.AgeBand ?? string.Empty,
                ReadingLevel = story?.ReadingLevel,
                IsEligible = RoadmapEligibility.IsEligible(story)
            };
        }).ToList();
    }
}
