using StoryPlatform.Application.Features.ContentReports.DTOs;

namespace StoryPlatform.Application.Features.ContentReports.Interfaces;

/// <summary>Phía người giám sát của WF-L4E: báo cáo truyện đã công khai.</summary>
public interface IContentReportService
{
    Task<ContentReportResultDto> CreateAsync(
        int reporterUserId, CreateContentReportRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moderator tạo mục rà soát cho nội dung Public Library đang công khai. Mục đi vào hàng đợi cho Moderator khác;
    /// người tạo không thấy và không nhận được mục.
    /// </summary>
    Task<ContentReportResultDto> CreateReviewItemAsync(
        int moderatorUserId, CreateReviewItemRequestDto request, CancellationToken cancellationToken = default);
}
