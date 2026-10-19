using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.RoadmapTemplates.DTOs;
using StoryPlatform.Application.Features.RoadmapTemplates.Interfaces;

namespace StoryPlatform.Api.Controllers;

[Authorize(Roles = "Moderator,Administrator")]
public class RoadmapTemplateController : BaseApiController
{
    private readonly IRoadmapTemplateService _roadmapService;

    public RoadmapTemplateController(IRoadmapTemplateService roadmapService)
    {
        _roadmapService = roadmapService;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<RoadmapListItemDto>>>> List(
        [FromQuery] RoadmapListQueryDto query, CancellationToken cancellationToken)
    {
        var result = await _roadmapService.ListAsync(GetCurrentUserId(), query, cancellationToken);
        return HandleResult(result, "Lấy danh sách bộ truyện mẫu thành công.");
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ApiResponse<RoadmapDetailDto>>> Get(
        int id, CancellationToken cancellationToken)
    {
        var result = await _roadmapService.GetAsync(GetCurrentUserId(), id, cancellationToken);
        return HandleResult(result, "Lấy chi tiết bộ truyện mẫu thành công.");
    }

    [HttpGet("candidates")]
    public async Task<ActionResult<ApiResponse<PagedResult<CandidateStoryDto>>>> ListCandidates(
        [FromQuery] CandidateQueryDto query, CancellationToken cancellationToken)
    {
        var result = await _roadmapService.ListCandidatesAsync(
            GetCurrentUserId(), query, cancellationToken);
        return HandleResult(result, "Lấy danh sách truyện ứng viên thành công.");
    }

    [Authorize(Roles = "Moderator")]
    [HttpPost]
    public async Task<ActionResult<ApiResponse<RoadmapDetailDto>>> Create(
        [FromBody] SaveRoadmapRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _roadmapService.CreateAsync(GetCurrentUserId(), request, cancellationToken);
        return HandleResult(result, "Đã tạo bộ truyện mẫu.");
    }

    [Authorize(Roles = "Moderator")]
    [HttpPut("{id:int}")]
    public async Task<ActionResult<ApiResponse<RoadmapDetailDto>>> Update(
        int id, [FromBody] SaveRoadmapRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _roadmapService.UpdateAsync(GetCurrentUserId(), id, request, cancellationToken);
        return HandleResult(result, "Đã cập nhật bộ truyện mẫu.");
    }

    [Authorize(Roles = "Moderator")]
    [HttpPut("{id:int}/items")]
    public async Task<ActionResult<ApiResponse<RoadmapDetailDto>>> SetItems(
        int id, [FromBody] SetRoadmapItemsRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _roadmapService.SetItemsAsync(GetCurrentUserId(), id, request, cancellationToken);
        return HandleResult(result, "Đã lưu danh sách truyện của bộ.");
    }

    [Authorize(Roles = "Moderator")]
    [HttpPost("{id:int}/submit")]
    public async Task<ActionResult<ApiResponse<RoadmapSubmitResultDto>>> Submit(
        int id, CancellationToken cancellationToken)
    {
        var result = await _roadmapService.SubmitAsync(GetCurrentUserId(), id, cancellationToken);
        return HandleResult(result, "Đã gửi bộ truyện mẫu đi duyệt.");
    }

    [Authorize(Roles = "Moderator")]
    [HttpPost("{id:int}/withdraw")]
    public async Task<ActionResult<ApiResponse<RoadmapSubmitResultDto>>> Withdraw(
        int id, CancellationToken cancellationToken)
    {
        var result = await _roadmapService.WithdrawAsync(GetCurrentUserId(), id, cancellationToken);
        return HandleResult(result, "Đã rút yêu cầu duyệt.");
    }
}
