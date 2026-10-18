using StoryPlatform.Application.Features.Moderation.DTOs;

namespace StoryPlatform.Application.Features.Moderation.Interfaces;

public interface IPublicStoryReviewService
{
    Task<PublicReviewPackageDto> GetPackageAsync(
        int actorUserId, int itemId, CancellationToken cancellationToken = default);

    Task<PublicReviewProgressDto> MarkViewedAsync(
        int actorUserId, int itemId, MarkPartViewedRequestDto request, CancellationToken cancellationToken = default);

    Task<PublicReviewFlagsResultDto> ResolveFlagAsync(
        int actorUserId, int itemId, ResolveFlagRequestDto request, CancellationToken cancellationToken = default);

    Task<PublicReviewChecklistDto> SaveChecklistAsync(
        int actorUserId, int itemId, PublicReviewChecklistDto input, CancellationToken cancellationToken = default);

    Task<List<PublicReviewLabelDto>> SetContentLabelsAsync(
        int actorUserId, int itemId, SetContentLabelsRequestDto request, CancellationToken cancellationToken = default);
}
