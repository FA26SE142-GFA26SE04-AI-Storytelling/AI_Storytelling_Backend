using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Application.Abstractions.AI;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Api.Authorization;
using StoryPlatform.Application.Features.AccessControl;

namespace StoryPlatform.Api.Controllers;

[ApiController]
[HasPermission(Permissions.Shared.MediaGenerate)]
[Route("api/v1/ai-generation")]
public sealed class AiGenerationCapacityController(IAccountAiActivityService aiActivityService) : ControllerBase
{
    [HttpGet("capacity")]
    public async Task<ActionResult<ApiResponse<AiGenerationCapacityDto>>> GetCapacity(CancellationToken cancellationToken)
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!int.TryParse(value, out var userId))
        {
            throw new UnauthorizedAccessException();
        }

        var result = await aiActivityService.GetCapacityAsync(userId, cancellationToken);
        return Ok(ApiResponse<AiGenerationCapacityDto>.Ok(result, "Lấy thông tin giới hạn tạo truyện AI thành công."));
    }
}
