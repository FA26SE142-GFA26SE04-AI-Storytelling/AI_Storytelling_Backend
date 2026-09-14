using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.Outline.DTOs;
using StoryPlatform.Application.Features.Outline.Interfaces;

namespace StoryPlatform.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/stories/{storyId:int}/outline")]
public sealed class OutlineController : ControllerBase
{
    private readonly IOutlineService _service;

    public OutlineController(IOutlineService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<OutlineProgressDto>>> GetCurrent(
        int storyId, CancellationToken cancellationToken)
    {
        var result = await _service.GetCurrentAsync(CurrentUserId(), storyId, cancellationToken);
        return Ok(ApiResponse<OutlineProgressDto>.Ok(result));
    }

    [HttpGet("versions")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<OutlineVersionDto>>>> GetVersions(
        int storyId, CancellationToken cancellationToken)
    {
        var result = await _service.GetVersionsAsync(CurrentUserId(), storyId, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<OutlineVersionDto>>.Ok(result));
    }

    [HttpGet("versions/{versionNo:int}")]
    public async Task<ActionResult<ApiResponse<OutlineVersionDto>>> GetVersion(
        int storyId, int versionNo, CancellationToken cancellationToken)
    {
        var result = await _service.GetVersionAsync(CurrentUserId(), storyId, versionNo, cancellationToken);
        return Ok(ApiResponse<OutlineVersionDto>.Ok(result));
    }

    [HttpPut("versions/{versionNo:int}")]
    public async Task<ActionResult<ApiResponse<OutlineVersionDto>>> Edit(
        int storyId, int versionNo, [FromBody] EditOutlineRequestDto input, CancellationToken cancellationToken)
    {
        var result = await _service.EditAsync(CurrentUserId(), storyId, versionNo, input, cancellationToken);
        return Ok(ApiResponse<OutlineVersionDto>.Ok(result, "Đã tạo phiên bản outline chỉnh sửa mới."));
    }

    [HttpPost("versions/{versionNo:int}/regenerate")]
    public async Task<ActionResult<ApiResponse<OutlineProgressDto>>> Regenerate(
        int storyId, int versionNo, [FromBody] RegenerateOutlineRequestDto input, CancellationToken cancellationToken)
    {
        var result = await _service.RegenerateAsync(CurrentUserId(), storyId, versionNo, input, cancellationToken);
        return Accepted(ApiResponse<OutlineProgressDto>.Ok(result, "Đã ghi nhận yêu cầu regenerate outline."));
    }

    [HttpPost("retry")]
    public async Task<ActionResult<ApiResponse<OutlineProgressDto>>> RetryInitial(
        int storyId, [FromBody] RetryOutlineRequestDto input, CancellationToken cancellationToken)
    {
        var result = await _service.RetryInitialAsync(CurrentUserId(), storyId, input, cancellationToken);
        return Accepted(ApiResponse<OutlineProgressDto>.Ok(result, "Đã ghi nhận yêu cầu retry initial outline."));
    }

    [HttpPost("versions/{versionNo:int}/approve")]
    public async Task<ActionResult<ApiResponse<OutlineProgressDto>>> Approve(
        int storyId, int versionNo, [FromBody] ApproveOutlineRequestDto input, CancellationToken cancellationToken)
    {
        var result = await _service.ApproveAsync(CurrentUserId(), storyId, versionNo, input, cancellationToken);
        return Accepted(ApiResponse<OutlineProgressDto>.Ok(result, "Outline đã được duyệt và đang chờ Phase 3."));
    }

    [HttpPost("versions/{versionNo:int}/reject")]
    public async Task<ActionResult<ApiResponse<OutlineProgressDto>>> Reject(
        int storyId, int versionNo, [FromBody] RejectOutlineRequestDto input, CancellationToken cancellationToken)
    {
        var result = await _service.RejectAsync(CurrentUserId(), storyId, versionNo, input, cancellationToken);
        return Ok(ApiResponse<OutlineProgressDto>.Ok(result, "Outline đã bị từ chối."));
    }

    private int CurrentUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return int.TryParse(value, out var userId) ? userId : throw new UnauthorizedAccessException();
    }
}
