using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ChildRoadmaps.Services;

/// <summary>A manual roadmap assignment records an authorized supervisor's story selection.</summary>
public static class ParentStorySelection
{
    public static Task<bool> ExistsAsync(IUnitOfWork unitOfWork, int childId, int storyId, CancellationToken ct) =>
        unitOfWork.Repository<ChildRoadmapItem>().ExistsAsync(item => !item.IsDeleted
            && item.ChildProfileId == childId && item.StoryId == storyId
            && item.Source == ChildRoadmapItemSource.Manual
            && (item.Status == ChildRoadmapItemStatus.Waiting || item.Status == ChildRoadmapItemStatus.Open
                || item.Status == ChildRoadmapItemStatus.Completed), ct);
}
