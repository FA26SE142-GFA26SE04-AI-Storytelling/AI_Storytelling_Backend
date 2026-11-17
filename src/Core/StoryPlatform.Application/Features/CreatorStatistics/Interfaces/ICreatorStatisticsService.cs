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

    /// <summary>
    /// 4 tiêu chí Creator đối tác (Luồng 5, 5g) trên cửa sổ 90 ngày kết thúc lúc <paramref name="now"/>, cho từng Creator.
    /// Dữ liệu đọc chung được tải một lần nên xét cả danh sách Creator rẻ hơn gọi từng người.
    /// </summary>
    Task<IReadOnlyDictionary<int, PartnerProgressDto>> GetPartnerProgressAsync(
        IReadOnlyCollection<int> creatorUserIds, DateTime now, CancellationToken cancellationToken = default);

    /// <summary>
    /// Thu nhập của mọi Creator trong kỳ, cùng công thức với <see cref="GetRevenueAsync"/> (quỹ × lượt đọc hợp lệ của
    /// Creator ÷ tổng lượt đọc hợp lệ, làm tròn xuống). Dùng khi chốt kỳ chia tiền (Luồng 5, 5h).
    /// </summary>
    Task<CreatorPeriodEarningsDto> GetPeriodEarningsAsync(
        DateTime periodStart, DateTime periodEnd, CancellationToken cancellationToken = default);

    /// <summary>Thống kê Creator cho Administrator (Luồng 5, 5e): tổng hợp của kỳ và từng Creator.</summary>
    Task<AdminCreatorStatisticsDto> GetAdminOverviewAsync(
        DateTime periodStart, DateTime periodEnd, CancellationToken cancellationToken = default);
}
