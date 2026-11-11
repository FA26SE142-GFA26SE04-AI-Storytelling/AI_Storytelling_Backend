using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.BusinessReports.DTOs;

namespace StoryPlatform.Application.Features.BusinessReports.Interfaces;

/// <summary>
/// Tổng hợp và publish Business Report định kỳ (Bước 5.5), kèm số liệu doanh thu của kỳ (F-15).
/// </summary>
public interface IBusinessReportService
{
    /// <summary>Tạo báo cáo Compiling cho một kỳ không chồng lên báo cáo đã có; chụp số liệu doanh thu của kỳ.</summary>
    Task<AdminBusinessReportDto> GenerateAsync(
        int adminUserId, GenerateBusinessReportRequestDto request, CancellationToken cancellationToken = default);

    Task<AdminBusinessReportDto> PublishAsync(
        int adminUserId, int reportId, CancellationToken cancellationToken = default);

    /// <summary>Báo cáo publish gần nhất cho mọi tài khoản đã đăng nhập; không có số liệu doanh thu.</summary>
    Task<BusinessReportDto> GetLatestPublishedAsync(CancellationToken cancellationToken = default);

    /// <summary>Mọi báo cáo, kỳ mới nhất trước, cho Administrator.</summary>
    Task<PagedResult<AdminBusinessReportDto>> ListAsync(
        int pageIndex, int pageSize, CancellationToken cancellationToken = default);

    Task<AdminBusinessReportDto> GetByIdAsync(int reportId, CancellationToken cancellationToken = default);
}
