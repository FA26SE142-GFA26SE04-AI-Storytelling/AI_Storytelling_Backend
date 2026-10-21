using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.ChildRoadmaps;
using StoryPlatform.Application.Features.ChildRoadmaps.Interfaces;

namespace StoryPlatform.Api.Controllers;

/// <summary>Kho bộ truyện mẫu của phụ huynh và việc chép bộ vào lộ trình.</summary>
[Authorize(Roles = "Parent")]
public class RoadmapLibraryController : BaseApiController
{
    private readonly IRoadmapLibraryService _libraryService;

    public RoadmapLibraryController(IRoadmapLibraryService libraryService)
    {
        _libraryService = libraryService;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<RoadmapLibraryItemDto>>>> List(
        [FromQuery] RoadmapLibraryQueryDto query, CancellationToken cancellationToken)
    {
        var result = await _libraryService.ListAsync(GetCurrentUserId(), query, cancellationToken);
        return HandleResult(result, "Lấy Kho bộ truyện mẫu thành công.");
    }

    [HttpGet("{roadmapId:int}")]
    public async Task<ActionResult<ApiResponse<RoadmapLibraryDetailDto>>> Get(
        int roadmapId, CancellationToken cancellationToken)
    {
        var result = await _libraryService.GetAsync(GetCurrentUserId(), roadmapId, cancellationToken);
        return HandleResult(result, "Lấy chi tiết bộ truyện mẫu thành công.");
    }

    [HttpPost("{roadmapId:int}/preview")]
    public async Task<ActionResult<ApiResponse<RoadmapCopyPlanDto>>> Preview(
        int roadmapId, [FromBody] RoadmapCopyRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _libraryService.PreviewAsync(GetCurrentUserId(), roadmapId, request, cancellationToken);
        return HandleResult(result, "Đã dựng bản xem trước việc chép bộ.");
    }

    [HttpPost("{roadmapId:int}/copy")]
    public async Task<ActionResult<ApiResponse<RoadmapCopyPlanDto>>> Copy(
        int roadmapId, [FromBody] RoadmapCopyRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _libraryService.CopyAsync(GetCurrentUserId(), roadmapId, request, cancellationToken);
        return HandleResult(result, "Đã chép bộ truyện mẫu vào lộ trình.");
    }
}
