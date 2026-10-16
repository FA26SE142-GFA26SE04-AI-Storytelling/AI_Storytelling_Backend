using StoryPlatform.Application.Features.Moderation.DTOs;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Application.Features.Moderation.Interfaces;

/// <summary>
/// Hàng đợi kiểm duyệt dùng chung: tạo mục, xem, nhận, đọc mục đang nhận (Moderator Mục 2).
/// </summary>
public interface IModerationQueueService
{
    /// <summary>Đưa một mục mới vào hàng đợi với trạng thái Pending, ưu tiên và hạn SLA theo loại.</summary>
    Task<ModerationItem> EnqueueAsync(
        EnqueueModerationItemCommand command, CancellationToken cancellationToken = default);

    /// <summary>Xem hàng đợi (lọc theo loại, ưu tiên, trạng thái, quá hạn; mục cao và gần hạn lên đầu).</summary>
    Task<PagedResult<ModerationItemSummaryDto>> ListAsync(
        int actorUserId, ModerationQueueQueryDto query, CancellationToken cancellationToken = default);

    /// <summary>Nhận một mục Pending (Pending → Claimed). Hai người không nhận trùng; không nhận mục của chính mình.</summary>
    Task<ModerationItemDetailDto> ClaimAsync(
        int actorUserId, int itemId, CancellationToken cancellationToken = default);

    /// <summary>Mở mục đang do chính mình nhận (trong 2 giờ); ghi audit mỗi lượt mở.</summary>
    Task<ModerationItemDetailDto> OpenAsync(
        int actorUserId, int itemId, CancellationToken cancellationToken = default);
}
