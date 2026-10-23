using StoryPlatform.Application.Common.Models;

namespace StoryPlatform.Application.Features.ReadingSessions.Interfaces;

public interface IParentReadingFeedbackService
{
    Task<PagedResult<ParentReadingFeedbackItemDto>> ListAsync(
        int parentUserId, int childProfileId, ParentReadingFeedbackQueryDto query, CancellationToken ct = default);
}
