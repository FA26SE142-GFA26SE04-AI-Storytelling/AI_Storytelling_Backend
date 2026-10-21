using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.RoadmapTemplates.DTOs;
using StoryPlatform.Application.Features.RoadmapTemplates.Interfaces;

namespace StoryPlatform.Api.Controllers;

[Authorize(Roles = "Moderator,Administrator")]
public class RoadmapReviewController : BaseApiController
{
    private readonly IRoadmapReviewService _reviewService;

    public RoadmapReviewController(IRoadmapReviewService reviewService)
    {
        _reviewService = reviewService;
    }

    [HttpGet("{itemId:int}")]
    public async Task<ActionResult<ApiResponse<RoadmapReviewPackageDto>>> GetPackage(
        int itemId, CancellationToken cancellationToken)
    {
        var result = await _reviewService.GetPackageAsync(
            GetCurrentUserId(), itemId, cancellationToken);
        return HandleResult(result, "Lấy gói duyệt bộ truyện mẫu thành công.");
    }

    [HttpPut("{itemId:int}/checklist")]
    public async Task<ActionResult<ApiResponse<List<RoadmapChecklistEntryDto>>>> SaveChecklist(
        int itemId,
        [FromBody] SaveRoadmapChecklistRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await _reviewService.SaveChecklistAsync(
            GetCurrentUserId(), itemId, request, cancellationToken);
        return HandleResult(result, "Đã lưu checklist.");
    }

    [HttpPost("{itemId:int}/approve")]
    public async Task<ActionResult<ApiResponse<RoadmapReviewDecisionResultDto>>> Approve(
        int itemId, CancellationToken cancellationToken)
    {
        var result = await _reviewService.ApproveAsync(
            GetCurrentUserId(), itemId, cancellationToken);
        return HandleResult(result, "Đã duyệt và công khai bộ truyện mẫu.");
    }

    [HttpPost("{itemId:int}/request-changes")]
    public async Task<ActionResult<ApiResponse<RoadmapReviewDecisionResultDto>>> RequestChanges(
        int itemId,
        [FromBody] RequestRoadmapChangesDto request,
        CancellationToken cancellationToken)
    {
        var result = await _reviewService.RequestChangesAsync(
            GetCurrentUserId(), itemId, request, cancellationToken);
        return HandleResult(result, "Đã yêu cầu người tạo sửa bộ truyện mẫu.");
    }

    [HttpPost("{itemId:int}/reject")]
    public async Task<ActionResult<ApiResponse<RoadmapReviewDecisionResultDto>>> Reject(
        int itemId,
        [FromBody] RejectRoadmapRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await _reviewService.RejectAsync(
            GetCurrentUserId(), itemId, request, cancellationToken);
        return HandleResult(result, "Đã từ chối bộ truyện mẫu.");
    }
}
