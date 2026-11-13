using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.StoryRatings.DTOs;
using StoryPlatform.Application.Features.StoryRatings.Interfaces;

namespace StoryPlatform.Api.Controllers;

/// <summary>
/// Moderator xử lý mục StoryRating đang nhận (claim qua ModerationQueue). Administrator không xử lý loại mục này.
/// </summary>
[Authorize(Roles = "Moderator")]
public class StoryRatingModerationController : BaseApiController
{
    private readonly IStoryRatingModerationService _moderationService;

    public StoryRatingModerationController(IStoryRatingModerationService moderationService)
    {
        _moderationService = moderationService;
    }

    /// <summary>Chi tiết đánh giá, lý do bị giữ và lý do báo cáo (không kèm người báo cáo).</summary>
    [HttpGet("{itemId:int}")]
    public async Task<ActionResult<ApiResponse<StoryRatingModerationDetailDto>>> GetDetail(
        int itemId, CancellationToken cancellationToken)
    {
        var result = await _moderationService.GetDetailAsync(GetCurrentUserId(), itemId, cancellationToken);
        return HandleResult(result, "Lấy chi tiết đánh giá thành công.");
    }

    /// <summary>Duyệt: đánh giá đang chờ được hiện, đánh giá đang hiện được giữ; báo cáo được đóng.</summary>
    [HttpPost("{itemId:int}/approve")]
    public async Task<ActionResult<ApiResponse<StoryRatingDecisionResultDto>>> Approve(
        int itemId, CancellationToken cancellationToken)
    {
        var result = await _moderationService.ApproveAsync(GetCurrentUserId(), itemId, cancellationToken);
        return HandleResult(result, "Đã duyệt đánh giá.");
    }

    /// <summary>Ẩn đánh giá; lý do bắt buộc và được gửi cho người viết.</summary>
    [HttpPost("{itemId:int}/hide")]
    public async Task<ActionResult<ApiResponse<StoryRatingDecisionResultDto>>> Hide(
        int itemId, [FromBody] HideStoryRatingRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _moderationService.HideAsync(GetCurrentUserId(), itemId, request, cancellationToken);
        return HandleResult(result, "Đã ẩn đánh giá.");
    }
}
