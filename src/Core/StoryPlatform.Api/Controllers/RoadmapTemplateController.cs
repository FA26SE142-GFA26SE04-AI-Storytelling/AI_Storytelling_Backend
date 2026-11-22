using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.RoadmapTemplates.DTOs;
using StoryPlatform.Application.Features.RoadmapTemplates.Interfaces;
using StoryPlatform.Api.Authorization;
using StoryPlatform.Application.Features.AccessControl;

namespace StoryPlatform.Api.Controllers;

[HasPermission(Permissions.Review.RoadmapTemplateView)]
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

    [HasPermission(Permissions.Review.RoadmapTemplateAuthor)]
    [HttpPost]
    public async Task<ActionResult<ApiResponse<RoadmapDetailDto>>> Create(
        [FromBody] SaveRoadmapRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _roadmapService.CreateAsync(GetCurrentUserId(), request, cancellationToken);
        return HandleResult(result, "Đã tạo bộ truyện mẫu.");
    }

    [HasPermission(Permissions.Review.RoadmapTemplateAuthor)]
    [HttpPut("{id:int}")]
    public async Task<ActionResult<ApiResponse<RoadmapDetailDto>>> Update(
        int id, [FromBody] SaveRoadmapRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _roadmapService.UpdateAsync(GetCurrentUserId(), id, request, cancellationToken);
        return HandleResult(result, "Đã cập nhật bộ truyện mẫu.");
    }

    [HasPermission(Permissions.Review.RoadmapTemplateAuthor)]
    [HttpPut("{id:int}/items")]
    public async Task<ActionResult<ApiResponse<RoadmapDetailDto>>> SetItems(
        int id, [FromBody] SetRoadmapItemsRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _roadmapService.SetItemsAsync(GetCurrentUserId(), id, request, cancellationToken);
        return HandleResult(result, "Đã lưu danh sách truyện của bộ.");
    }

    [HasPermission(Permissions.Review.RoadmapTemplateAuthor)]
    [HttpPost("{id:int}/submit")]
    public async Task<ActionResult<ApiResponse<RoadmapSubmitResultDto>>> Submit(
        int id, CancellationToken cancellationToken)
    {
        var result = await _roadmapService.SubmitAsync(GetCurrentUserId(), id, cancellationToken);
        return HandleResult(result, "Đã gửi bộ truyện mẫu đi duyệt.");
    }

    [HasPermission(Permissions.Review.RoadmapTemplateAuthor)]
    [HttpPost("{id:int}/withdraw")]
    public async Task<ActionResult<ApiResponse<RoadmapSubmitResultDto>>> Withdraw(
        int id, CancellationToken cancellationToken)
    {
        var result = await _roadmapService.WithdrawAsync(GetCurrentUserId(), id, cancellationToken);
        return HandleResult(result, "Đã rút yêu cầu duyệt.");
    }

    /// <summary>Nhân bản bộ công khai hoặc Nháp của Moderator đã bị vô hiệu hóa.</summary>
    [HasPermission(Permissions.Review.RoadmapTemplateAuthor)]
    [HttpPost("{id:int}/clone")]
    public async Task<ActionResult<ApiResponse<RoadmapCloneResultDto>>> Clone(
        int id, CancellationToken cancellationToken)
    {
        var result = await _roadmapService.CloneAsync(GetCurrentUserId(), id, cancellationToken);
        return HandleResult(result, result.AlreadyExisted
            ? "Bộ này đã có bản nhân bản đang mở."
            : "Đã nhân bản bộ truyện mẫu.");
    }

    /// <summary>Archive bộ đang công khai với lý do bắt buộc từ 3 đến 500 ký tự.</summary>
    [HasPermission(Permissions.Review.RoadmapTemplateAuthor)]
    [HttpPost("{id:int}/archive")]
    public async Task<ActionResult<ApiResponse<RoadmapDetailDto>>> Archive(
        int id, [FromBody] ArchiveRoadmapRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _roadmapService.ArchiveAsync(
            GetCurrentUserId(), id, request, cancellationToken);
        return HandleResult(result, "Đã Archive bộ truyện mẫu.");
    }
}
