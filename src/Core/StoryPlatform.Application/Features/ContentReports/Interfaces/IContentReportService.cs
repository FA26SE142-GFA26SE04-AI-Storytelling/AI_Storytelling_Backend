using StoryPlatform.Application.Features.ContentReports.DTOs;

namespace StoryPlatform.Application.Features.ContentReports.Interfaces;

/// <summary>Phía người giám sát của WF-L4E: báo cáo truyện đã công khai.</summary>
public interface IContentReportService
{
    Task<ContentReportResultDto> CreateAsync(
        int reporterUserId, CreateContentReportRequestDto request, CancellationToken cancellationToken = default);
}
