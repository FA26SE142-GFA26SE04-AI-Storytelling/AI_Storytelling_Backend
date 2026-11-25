using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.ContentReports.DTOs;
using StoryPlatform.Application.Features.ContentReports.Interfaces;
using StoryPlatform.Api.Authorization;
using StoryPlatform.Application.Features.AccessControl;

namespace StoryPlatform.Api.Controllers;

/// <summary>
/// Moderator xử lý report truyện (WF-L4E, UC-L4E-02). itemId là mục ContentReport mà người gọi đang nhận.
/// Chỉ Moderator; Administrator không xử lý report.
/// </summary>
[HasPermission(Permissions.Moderation.ReportReview)]
public class ContentReportReviewController : BaseApiController
{
    private readonly IContentReportReviewService _reviewService;

    public ContentReportReviewController(IContentReportReviewService reviewService)
    {
        _reviewService = reviewService;
    }

    /// <summary>Chi tiết report và truyện bị báo cáo; nội dung đã che thông tin cá nhân.</summary>
    [HttpGet("{itemId:int}")]
    public async Task<ActionResult<ApiResponse<ContentReportDetailDto>>> GetDetail(
        int itemId, CancellationToken cancellationToken)
    {
        var result = await _reviewService.GetDetailAsync(
            GetCurrentUserId(), itemId, cancellationToken);
        return HandleResult(result, "Lấy chi tiết báo cáo thành công.");
    }

    /// <summary>Bỏ qua report vì không vi phạm; lý do là bắt buộc.</summary>
    [HttpPost("{itemId:int}/dismiss")]
    public async Task<ActionResult<ApiResponse<ContentReportDecisionResultDto>>> Dismiss(
        int itemId,
        [FromBody] DismissReportRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await _reviewService.DismissAsync(
            GetCurrentUserId(), itemId, request, cancellationToken);
        return HandleResult(result, "Đã bỏ qua báo cáo.");
    }

    /// <summary>Gỡ truyện (Archive), không xóa cứng; lý do gỡ và lý do chi tiết là bắt buộc.</summary>
    [HttpPost("{itemId:int}/archive")]
    public async Task<ActionResult<ApiResponse<ContentReportDecisionResultDto>>> Archive(
        int itemId,
        [FromBody] ArchiveReportRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await _reviewService.ArchiveAsync(
            GetCurrentUserId(), itemId, request, cancellationToken);
        return HandleResult(result, "Đã gỡ truyện.");
    }
}
