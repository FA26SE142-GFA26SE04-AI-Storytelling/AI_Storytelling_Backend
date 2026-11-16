using StoryPlatform.Application.Common.Models;

namespace StoryPlatform.Application.Features.ReadingSessions.Interfaces;

public interface IParentReadingFeedbackService
{
    Task<PagedResult<ParentReadingFeedbackItemDto>> ListAsync(
        int parentUserId, int childProfileId, ParentReadingFeedbackQueryDto query, CancellationToken ct = default);

    Task<ParentQuizDetailDto> GetQuizAsync(
        int parentUserId, int childProfileId, int sessionId, CancellationToken ct = default);

    Task<ParentReadingOverviewDto> GetOverviewAsync(
        int parentUserId, int childProfileId, ParentReadingOverviewQueryDto query, CancellationToken ct = default);
}
