using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.ChildRoadmaps;
using StoryPlatform.Application.Features.ChildRoadmaps.Interfaces;

namespace StoryPlatform.Api.Controllers;

/// <summary>Lộ trình của con, phía phụ huynh (Luồng 4 — WF-L4A).</summary>
[Authorize(Roles = "Parent")]
public class ChildRoadmapController : BaseApiController
{
    private readonly IChildRoadmapService _roadmapService;

    public ChildRoadmapController(IChildRoadmapService roadmapService)
    {
        _roadmapService = roadmapService;
    }

    [HttpPost("stories")]
    public async Task<ActionResult<ApiResponse<AddRoadmapStoryResultDto>>> AddStory(
        [FromBody] AddRoadmapStoryRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _roadmapService.AddStoryAsync(GetCurrentUserId(), request, cancellationToken);
        return HandleResult(result, "Đã xử lý yêu cầu thêm truyện vào lộ trình.");
    }

    [HttpGet("{childProfileId:int}")]
    public async Task<ActionResult<ApiResponse<ChildRoadmapDto>>> Get(
        int childProfileId, CancellationToken cancellationToken)
    {
        var result = await _roadmapService.GetAsync(GetCurrentUserId(), childProfileId, cancellationToken);
        return HandleResult(result, "Lấy lộ trình của con thành công.");
    }

    [HttpDelete("{childProfileId:int}/items/{itemId:int}")]
    public async Task<ActionResult<ApiResponse<ChildRoadmapDto>>> Remove(
        int childProfileId, int itemId, CancellationToken cancellationToken)
    {
        var result = await _roadmapService.RemoveAsync(GetCurrentUserId(), childProfileId, itemId, cancellationToken);
        return HandleResult(result, "Đã gỡ truyện khỏi lộ trình.");
    }

    [HttpPut("{childProfileId:int}/order")]
    public async Task<ActionResult<ApiResponse<ChildRoadmapDto>>> Reorder(
        int childProfileId, [FromBody] ReorderRoadmapRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _roadmapService.ReorderAsync(GetCurrentUserId(), childProfileId, request, cancellationToken);
        return HandleResult(result, "Đã đổi thứ tự lộ trình.");
    }

    [HttpDelete("{childProfileId:int}/items")]
    public async Task<ActionResult<ApiResponse<ChildRoadmapDto>>> Clear(
        int childProfileId, CancellationToken cancellationToken)
    {
        var result = await _roadmapService.ClearAsync(GetCurrentUserId(), childProfileId, cancellationToken);
        return HandleResult(result, "Đã làm trống lộ trình.");
    }
}
