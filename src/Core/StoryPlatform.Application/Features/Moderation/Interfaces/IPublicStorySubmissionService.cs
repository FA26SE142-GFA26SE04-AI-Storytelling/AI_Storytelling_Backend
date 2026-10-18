using StoryPlatform.Application.Features.Moderation.DTOs;

namespace StoryPlatform.Application.Features.Moderation.Interfaces;

public interface IPublicStorySubmissionService
{
    Task<PublicStorySubmissionResultDto> SubmitAsync(
        int actorUserId,
        SubmitPublicStoryRequestDto request,
        CancellationToken cancellationToken = default);

    Task<PublicStorySubmissionResultDto> WithdrawAsync(
        int actorUserId,
        int itemId,
        CancellationToken cancellationToken = default);
}
