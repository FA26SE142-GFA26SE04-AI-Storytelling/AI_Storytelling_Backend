using StoryPlatform.Application.Features.BusinessReports.DTOs;

namespace StoryPlatform.Application.Features.BusinessReports.Interfaces;

/// <summary>
/// Thống kê tài khoản phụ huynh theo kỳ cho Administrator (F-16), là một phần của báo cáo kỳ.
/// </summary>
public interface IUserAccountStatisticsService
{
    /// <summary>Kỳ tính trọn ngày theo UTC, gồm cả ngày <paramref name="periodEnd"/>.</summary>
    Task<UserAccountStatisticsDto> GetAsync(
        DateTime periodStart, DateTime periodEnd, CancellationToken cancellationToken = default);
}
