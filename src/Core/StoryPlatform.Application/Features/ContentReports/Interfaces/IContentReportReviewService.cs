using StoryPlatform.Application.Features.ContentReports.DTOs;

namespace StoryPlatform.Application.Features.ContentReports.Interfaces;

/// <summary>Phía Moderator của WF-L4E UC-L4E-02: đọc và quyết định một mục report.</summary>
public interface IContentReportReviewService
{
    Task<ContentReportDetailDto> GetDetailAsync(
        int actorUserId, int itemId, CancellationToken cancellationToken = default);

    Task<ContentReportDecisionResultDto> DismissAsync(
        int actorUserId, int itemId, DismissReportRequestDto request,
        CancellationToken cancellationToken = default);

    Task<ContentReportDecisionResultDto> ArchiveAsync(
        int actorUserId, int itemId, ArchiveReportRequestDto request,
        CancellationToken cancellationToken = default);
}
