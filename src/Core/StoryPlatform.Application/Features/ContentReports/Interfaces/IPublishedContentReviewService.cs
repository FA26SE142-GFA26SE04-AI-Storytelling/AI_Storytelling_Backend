using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.ContentReports.DTOs;

namespace StoryPlatform.Application.Features.ContentReports.Interfaces;

/// <summary>Tab "Nội dung đã công khai" của Moderation view.</summary>
public interface IPublishedContentReviewService
{
    Task<PagedResult<PublishedContentItemDto>> ListAsync(
        int actorUserId, PublishedContentQueryDto query, CancellationToken cancellationToken = default);

    Task<PublishedStoryContentDto> GetStoryContentAsync(
        int actorUserId, int storyId, CancellationToken cancellationToken = default);

    Task<PublishedRoadmapDto> GetRoadmapAsync(
        int actorUserId, int roadmapId, CancellationToken cancellationToken = default);
}
