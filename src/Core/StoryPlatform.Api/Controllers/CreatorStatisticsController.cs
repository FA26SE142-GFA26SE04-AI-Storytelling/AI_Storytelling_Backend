using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.CreatorStatistics.DTOs;
using StoryPlatform.Application.Features.CreatorStatistics.Interfaces;

namespace StoryPlatform.Api.Controllers;

/// <summary>
/// Thống kê của Content Creator trên truyện của chính họ (F-20, F-21). Mã Creator luôn lấy từ token đăng nhập,
/// không nhận từ request; kết quả chỉ là số tổng hợp, không có dữ liệu của trẻ.
/// </summary>
[Authorize(Roles = "ContentCreator")]
public class CreatorStatisticsController : BaseApiController
{
    private readonly ICreatorStatisticsService _creatorStatisticsService;

    public CreatorStatisticsController(ICreatorStatisticsService creatorStatisticsService)
    {
        _creatorStatisticsService = creatorStatisticsService;
    }

    /// <summary>
    /// Lượt xem, yêu thích, đọc hoàn thành, đọc hợp lệ và tỷ lệ hoàn thành của từng truyện đang Published và tổng của kỳ.
    /// Truyền storyId để xem một truyện; truyện của Creator khác trả 403.
    /// </summary>
    [HttpGet("reading")]
    public async Task<ActionResult<ApiResponse<CreatorReadingStatisticsDto>>> GetReading(
        [FromQuery] DateTime periodStart, [FromQuery] DateTime periodEnd, [FromQuery] int? storyId,
        CancellationToken cancellationToken)
    {
        var result = await _creatorStatisticsService.GetReadingAsync(
            GetCurrentUserId(), periodStart, periodEnd, storyId, cancellationToken);
        return HandleResult(result, "Lấy thống kê lượt đọc thành công.");
    }

    /// <summary>
    /// Quỹ Creator của kỳ và thu nhập dự kiến của Creator: lượt đọc hợp lệ của mình × quỹ ÷ tổng lượt đọc hợp lệ,
    /// làm tròn xuống đến đồng, theo kỳ và theo từng truyện. Truyện của Creator khác trả 403.
    /// </summary>
    [HttpGet("revenue")]
    public async Task<ActionResult<ApiResponse<CreatorRevenueStatisticsDto>>> GetRevenue(
        [FromQuery] DateTime periodStart, [FromQuery] DateTime periodEnd, [FromQuery] int? storyId,
        CancellationToken cancellationToken)
    {
        var result = await _creatorStatisticsService.GetRevenueAsync(
            GetCurrentUserId(), periodStart, periodEnd, storyId, cancellationToken);
        return HandleResult(result, "Lấy thống kê thu nhập dự kiến thành công.");
    }
}
