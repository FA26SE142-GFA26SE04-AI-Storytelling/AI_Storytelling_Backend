using StoryPlatform.Application.Features.StoryRatings.DTOs;

namespace StoryPlatform.Application.Features.StoryRatings.Interfaces;

public interface IStoryRatingModerationService
{
    Task<StoryRatingModerationDetailDto> GetDetailAsync(
        int actorUserId, int itemId, CancellationToken cancellationToken = default);

    Task<StoryRatingDecisionResultDto> ApproveAsync(
        int actorUserId, int itemId, CancellationToken cancellationToken = default);

    Task<StoryRatingDecisionResultDto> HideAsync(
        int actorUserId, int itemId, HideStoryRatingRequestDto request, CancellationToken cancellationToken = default);
}
