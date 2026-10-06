using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.MediaGeneration.Interfaces;
using StoryPlatform.Application.Features.MediaGeneration.Models;

namespace StoryPlatform.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/stories/{storyId:int}/media")]
public sealed class MediaGenerationController : ControllerBase
{
    private readonly IMediaGenerationService _service;
    public MediaGenerationController(IMediaGenerationService service) => _service = service;

    [HttpGet("progress")]
    public async Task<ActionResult<ApiResponse<MediaGenerationProgress>>> GetProgress(
        int storyId, CancellationToken cancellationToken)
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!int.TryParse(value, out var userId)) throw new UnauthorizedAccessException();
        var result = await _service.GetProgressAsync(userId, storyId, cancellationToken);
        return Ok(ApiResponse<MediaGenerationProgress>.Ok(result));
    }

    [HttpGet("package")]
    [ProducesResponseType(typeof(ApiResponse<StoryMediaPackage>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ApiResponse<StoryMediaPackage>>> GetPackage(
        int storyId, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "private, no-store";
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!int.TryParse(value, out var userId)) throw new UnauthorizedAccessException();
        var result = await _service.GetPackageAsync(userId, storyId, cancellationToken);
        return Ok(ApiResponse<StoryMediaPackage>.Ok(result));
    }

    [HttpPost("illustration-beats/{beatId:int}/regenerate")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RegenerateIllustrationBeat(
        int storyId, int beatId, CancellationToken cancellationToken)
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!int.TryParse(value, out var userId)) throw new UnauthorizedAccessException();
        await _service.RegenerateIllustrationBeatAsync(userId, storyId, beatId, cancellationToken);
        return Accepted();
    }

    [HttpPost("retry")]
    [ProducesResponseType(typeof(ApiResponse<MediaGenerationProgress>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<MediaGenerationProgress>>> Retry(
        int storyId, CancellationToken cancellationToken)
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!int.TryParse(value, out var userId)) throw new UnauthorizedAccessException();
        var result = await _service.RetryAsync(userId, storyId, cancellationToken);
        return Ok(ApiResponse<MediaGenerationProgress>.Ok(result, "Đã đưa tác vụ media vào hàng đợi xử lý lại."));
    }
}
