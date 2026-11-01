using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.ChildRoadmaps.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ReadingSessions;

public static class ChildStoryLookup
{
    /// <summary>
    /// Truyện và bản hiện hành mà trẻ được mở theo storyId; null khi trẻ không có quyền với truyện.
    /// Truyện riêng của bé khác chỉ mở được khi đã có trong lộ trình thủ công của trẻ này.
    /// </summary>
    public static async Task<(Story Story, StoryVersion Version)?> FindAsync(
        IUnitOfWork unitOfWork, int childProfileId, int storyId, CancellationToken cancellationToken = default)
    {
        var story = await unitOfWork.Repository<Story>().GetByIdAsync(storyId, cancellationToken);
        if (story != null && story.Scope == StoryScope.Parent && story.ChildProfileId != childProfileId
            && !await ParentStorySelection.ExistsAsync(unitOfWork, childProfileId, story.Id, cancellationToken))
        {
            story = null;
        }

        var version = await unitOfWork.Repository<StoryVersion>().FirstOrDefaultAsync(
            value => value.StoryId == storyId && value.IsCurrent && !value.IsDeleted,
            cancellationToken: cancellationToken);

        if (story == null || story.IsDeleted || version == null || version.IsDeleted || version.StoryId != story.Id)
        {
            return null;
        }

        return (story, version);
    }
}
