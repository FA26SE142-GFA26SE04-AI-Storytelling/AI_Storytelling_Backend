using StoryPlatform.Application.Features.Moderation.DTOs;

namespace StoryPlatform.Application.Features.Moderation.Interfaces;

/// <summary>
/// Thống kê hàng đợi kiểm duyệt và SLA cho Moderator (F-19). Kỳ tính trọn ngày theo UTC, gồm cả ngày cuối.
/// </summary>
public interface IModerationStatisticsService
{
    /// <summary>
    /// Số mục đang chờ / đang nhận / quá hạn của toàn hàng đợi và số mục Moderator này đã quyết định trong kỳ.
    /// </summary>
    Task<ModerationStatisticsDto> GetAsync(
        int moderatorUserId, DateTime periodStart, DateTime periodEnd, CancellationToken cancellationToken = default);
}
