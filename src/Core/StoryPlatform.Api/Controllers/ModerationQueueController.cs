using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.Moderation.DTOs;
using StoryPlatform.Application.Features.Moderation.Interfaces;

namespace StoryPlatform.Api.Controllers;

/// <summary>
/// Hàng đợi kiểm duyệt dùng chung (Luồng 5 — Moderation view).
/// </summary>
[Authorize(Roles = "Moderator,Administrator")]
public class ModerationQueueController : BaseApiController
{
    private readonly IModerationQueueService _queueService;

    private readonly IModerationReviewViewService _reviewViewService;

    public ModerationQueueController(
        IModerationQueueService queueService, IModerationReviewViewService reviewViewService)
    {
        _queueService = queueService;
        _reviewViewService = reviewViewService;
    }

    /// <summary>"Xem để rà soát": cấp quyền đọc tạm 2 giờ cho nội dung đã công khai (ghi audit).</summary>
    [HttpPost("review-views")]
    public async Task<ActionResult<ApiResponse<ModerationReviewViewDto>>> GrantReviewView(
        [FromBody] GrantReviewViewRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _reviewViewService.GrantAsync(GetCurrentUserId(), request, cancellationToken);
        return HandleResult(result, "Đã cấp quyền xem trong 2 giờ.");
    }

    /// <summary>Xem hàng đợi: lọc theo loại, ưu tiên, trạng thái, quá hạn.</summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<ModerationItemSummaryDto>>>> List(
        [FromQuery] ModerationQueueQueryDto query, CancellationToken cancellationToken)
    {
        var result = await _queueService.ListAsync(GetCurrentUserId(), query, cancellationToken);
        return HandleResult(result, "Lấy hàng đợi kiểm duyệt thành công.");
    }

    /// <summary>Nhận xử lý một mục đang chờ.</summary>
    [HttpPost("{itemId:int}/claim")]
    public async Task<ActionResult<ApiResponse<ModerationItemDetailDto>>> Claim(
        int itemId, CancellationToken cancellationToken)
    {
        var result = await _queueService.ClaimAsync(GetCurrentUserId(), itemId, cancellationToken);
        return HandleResult(result, "Đã nhận xử lý mục kiểm duyệt.");
    }

    /// <summary>Mở mục đang do mình nhận (ghi audit mỗi lượt mở).</summary>
    [HttpGet("{itemId:int}")]
    public async Task<ActionResult<ApiResponse<ModerationItemDetailDto>>> Open(
        int itemId, CancellationToken cancellationToken)
    {
        var result = await _queueService.OpenAsync(GetCurrentUserId(), itemId, cancellationToken);
        return HandleResult(result, "Mở mục kiểm duyệt thành công.");
    }
}
