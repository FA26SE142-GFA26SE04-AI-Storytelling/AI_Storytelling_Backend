using StoryPlatform.Application.Features.CreatorStatistics.DTOs;

namespace StoryPlatform.Application.Features.CreatorStatistics.Interfaces;

/// <summary>
/// Thống kê của Content Creator trên truyện của chính họ (F-20, F-21). Kỳ tính trọn ngày theo UTC, gồm cả ngày cuối.
/// </summary>
public interface ICreatorStatisticsService
{
    /// <summary>Lượt xem, yêu thích, đọc hoàn thành, đọc hợp lệ và tỷ lệ hoàn thành theo truyện. Truyện của Creator khác → 403.</summary>
    Task<CreatorReadingStatisticsDto> GetReadingAsync(
        int creatorUserId, DateTime periodStart, DateTime periodEnd, int? storyId,
        CancellationToken cancellationToken = default);

    /// <summary>Quỹ Creator của kỳ và thu nhập dự kiến theo lượt đọc hợp lệ. Truyện của Creator khác → 403.</summary>
    Task<CreatorRevenueStatisticsDto> GetRevenueAsync(
        int creatorUserId, DateTime periodStart, DateTime periodEnd, int? storyId,
        CancellationToken cancellationToken = default);

    /// <summary>Số truyện theo trạng thái, kết quả gửi duyệt trong kỳ, lượt còn lại và tiến độ Creator đối tác (90 ngày). Chỉ của chính Creator.</summary>
    Task<CreatorContentStatisticsDto> GetContentAsync(
        int creatorUserId, DateTime periodStart, DateTime periodEnd, CancellationToken cancellationToken = default);
}
