using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.ContentGeneration.DTOs;
using StoryPlatform.Application.Features.ContentGeneration.Interfaces;

namespace StoryPlatform.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/stories/{storyId:int}/generation")]
public sealed class ContentGenerationController : ControllerBase
{
    private readonly IContentGenerationService _service;
    public ContentGenerationController(IContentGenerationService service) => _service = service;

    [HttpGet("progress")]
    [ProducesResponseType(typeof(ApiResponse<ContentGenerationProgressDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ContentGenerationProgressDto>>> GetProgress(
        int storyId, CancellationToken cancellationToken)
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!int.TryParse(value, out var userId)) throw new UnauthorizedAccessException();
        var result = await _service.GetProgressAsync(userId, storyId, cancellationToken);
        return Ok(ApiResponse<ContentGenerationProgressDto>.Ok(result));
    }

    [HttpPost("retry")]
    [ProducesResponseType(typeof(ApiResponse<ContentGenerationProgressDto>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<ContentGenerationProgressDto>>> Retry(
        int storyId,
        [FromBody] RetryContentGenerationRequestDto input,
        CancellationToken cancellationToken)
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!int.TryParse(value, out var userId)) throw new UnauthorizedAccessException();
        var result = await _service.RetryAsync(userId, storyId, input, cancellationToken);
        return Accepted(ApiResponse<ContentGenerationProgressDto>.Ok(
            result,
            "Đã ghi nhận yêu cầu retry sinh nội dung từ outline đã duyệt."));
    }
}
