using StoryPlatform.Application.Features.BusinessReports.DTOs;

namespace StoryPlatform.Application.Features.BusinessReports.Interfaces;

/// <summary>
/// Tổng hợp và publish Business Report định kỳ (Bước 5.5).
/// </summary>
public interface IBusinessReportService
{
    Task<BusinessReportDto> GenerateAsync(
        int adminUserId, GenerateBusinessReportRequestDto request, CancellationToken cancellationToken = default);

    Task<BusinessReportDto> PublishAsync(
        int adminUserId, int reportId, CancellationToken cancellationToken = default);

    Task<BusinessReportDto> GetLatestPublishedAsync(CancellationToken cancellationToken = default);
}
