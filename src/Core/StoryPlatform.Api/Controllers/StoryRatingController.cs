using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.StoryRatings.DTOs;
using StoryPlatform.Application.Features.StoryRatings.Interfaces;

namespace StoryPlatform.Api.Controllers;

/// <summary>
/// Phụ huynh đánh giá truyện Creator trong thư viện cộng đồng. Role đặt trên từng action
/// vì nhiều [Authorize(Roles)] lồng nhau là điều kiện AND.
/// </summary>
[Authorize]
public class StoryRatingController : BaseApiController
{
    private readonly IStoryRatingService _ratingService;

    public StoryRatingController(IStoryRatingService ratingService)
    {
        _ratingService = ratingService;
    }

    /// <summary>Đánh giá đang hiện của truyện, điểm tổng hợp, đánh giá của chính mình và quyền đánh giá.</summary>
    [Authorize(Roles = "User")]
    [HttpGet("story/{storyId:int}")]
    public async Task<ActionResult<ApiResponse<StoryRatingListDto>>> GetForStory(
        int storyId, [FromQuery] StoryRatingQueryDto query, CancellationToken cancellationToken)
    {
        var result = await _ratingService.GetForStoryAsync(GetCurrentUserId(), storyId, query, cancellationToken);
        return HandleResult(result, "Lấy đánh giá truyện thành công.");
    }

    /// <summary>Tạo hoặc sửa đánh giá của mình; bé phải đã mở đọc truyện.</summary>
    [Authorize(Roles = "User")]
    [HttpPut("story/{storyId:int}/me")]
    public async Task<ActionResult<ApiResponse<MyStoryRatingDto>>> UpsertMine(
        int storyId, [FromBody] UpsertStoryRatingRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _ratingService.UpsertMineAsync(GetCurrentUserId(), storyId, request, cancellationToken);
        return HandleResult(result, "Đã lưu đánh giá.");
    }

    /// <summary>Xóa đánh giá của mình (xóa mềm); mục kiểm duyệt đang mở được đóng.</summary>
    [Authorize(Roles = "User")]
    [HttpDelete("story/{storyId:int}/me")]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteMine(int storyId, CancellationToken cancellationToken)
    {
        var result = await _ratingService.DeleteMineAsync(GetCurrentUserId(), storyId, cancellationToken);
        return HandleResult(result, "Đã xóa đánh giá.");
    }

    /// <summary>Báo cáo một đánh giá đang hiện; mỗi người một lần. Creator chỉ báo cáo đánh giá truyện của mình.</summary>
    [Authorize(Roles = "User,ContentCreator")]
    [HttpPost("{ratingId:int}/reports")]
    public async Task<ActionResult<ApiResponse<StoryRatingReportResultDto>>> Report(
        int ratingId, [FromBody] ReportStoryRatingRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _ratingService.ReportAsync(GetCurrentUserId(), ratingId, request, cancellationToken);
        return HandleResult(result, "Đã gửi báo cáo đánh giá.");
    }

    /// <summary>Creator xem đánh giá đang hiện của truyện mình.</summary>
    [Authorize(Roles = "ContentCreator")]
    [HttpGet("/api/v1/creator/stories/{storyId:int}/ratings")]
    public async Task<ActionResult<ApiResponse<StoryRatingListDto>>> GetForMyStory(
        int storyId, [FromQuery] StoryRatingQueryDto query, CancellationToken cancellationToken)
    {
        var result = await _ratingService.GetForCreatorAsync(GetCurrentUserId(), storyId, query, cancellationToken);
        return HandleResult(result, "Lấy đánh giá truyện thành công.");
    }
}
