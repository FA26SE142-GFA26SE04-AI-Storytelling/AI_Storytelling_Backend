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

    Task<PublicReviewDecisionResultDto> ApproveAsync(
        int actorUserId, int itemId, CancellationToken cancellationToken = default);

    Task<PublicReviewDecisionResultDto> RequestChangesAsync(
        int actorUserId, int itemId, RequestChangesDto request, CancellationToken cancellationToken = default);

    Task<PublicReviewDecisionResultDto> RejectAsync(
        int actorUserId, int itemId, RejectRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>
    /// So sánh bản đang duyệt với bản đã gửi trước ("previous") hoặc bản đang công khai ("public").
    /// Chỉ thực hiện trên mục đang được người gọi giữ và ghi audit cho mỗi lượt so sánh.
    /// </summary>
    Task<PublicVersionComparisonDto> CompareAsync(
        int actorUserId, int itemId, string? against, CancellationToken cancellationToken = default);
}
