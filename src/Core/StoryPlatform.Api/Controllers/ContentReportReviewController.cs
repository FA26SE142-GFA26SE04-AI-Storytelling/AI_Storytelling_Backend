using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.ContentReports.DTOs;
using StoryPlatform.Application.Features.ContentReports.Interfaces;

namespace StoryPlatform.Api.Controllers;

/// <summary>
/// Moderator xử lý report truyện (WF-L4E, UC-L4E-02). itemId là mục ContentReport mà người gọi đang nhận.
/// </summary>
[Authorize(Roles = "Moderator,Administrator")]
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
        return HandleResult(result, "Lấy chi tiết report thành công.");
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
        return HandleResult(result, "Đã bỏ qua report.");
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

    /// <summary>Chuyển ca cho Administrator quyết định cuối. Chỉ Moderator đang giữ mục.</summary>
    [HttpPost("{itemId:int}/escalate")]
    public async Task<ActionResult<ApiResponse<ContentReportDecisionResultDto>>> Escalate(
        int itemId, [FromBody] EscalateReportRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _reviewService.EscalateAsync(
            GetCurrentUserId(), itemId, request, cancellationToken);
        return HandleResult(result, "Đã chuyển ca cho Administrator.");
    }
}
