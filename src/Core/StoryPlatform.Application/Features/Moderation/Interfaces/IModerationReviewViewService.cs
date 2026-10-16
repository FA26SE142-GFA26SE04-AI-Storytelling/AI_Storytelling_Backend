using StoryPlatform.Application.Features.Moderation.DTOs;

namespace StoryPlatform.Application.Features.Moderation.Interfaces;

/// <summary>
/// Quyền đọc tạm 2 giờ cho nội dung đã công khai, cấp khi Moderator bấm "Xem để rà soát" (ngoài hàng đợi).
/// </summary>
public interface IModerationReviewViewService
{
    Task<ModerationReviewViewDto> GrantAsync(
        int actorUserId, GrantReviewViewRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>True khi người này đang có quyền đọc còn hạn cho truyện hoặc bộ truyện mẫu đó.</summary>
    Task<bool> HasActiveViewAsync(
        int actorUserId, int? storyId, int? readingRoadmapId, CancellationToken cancellationToken = default);
}
