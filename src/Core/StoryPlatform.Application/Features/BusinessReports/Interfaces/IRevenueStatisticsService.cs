using StoryPlatform.Application.Features.BusinessReports.DTOs;

namespace StoryPlatform.Application.Features.BusinessReports.Interfaces;

/// <summary>
/// Thống kê doanh thu theo kỳ cho Administrator (F-15), tính trực tiếp từ giao dịch và sổ lượt.
/// </summary>
public interface IRevenueStatisticsService
{
    /// <summary>Kỳ tính trọn ngày theo UTC, gồm cả ngày <paramref name="periodEnd"/>.</summary>
    Task<RevenueStatisticsDto> GetAsync(
        DateTime periodStart, DateTime periodEnd, CancellationToken cancellationToken = default);
}
