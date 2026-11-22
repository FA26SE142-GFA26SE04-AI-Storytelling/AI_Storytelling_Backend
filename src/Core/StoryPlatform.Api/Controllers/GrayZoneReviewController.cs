using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Application.Features.Moderation.Services;
using StoryPlatform.Api.Authorization;
using StoryPlatform.Application.Features.AccessControl;

namespace StoryPlatform.Api.Controllers;

[HasPermission(Permissions.Review.GrayZone)]
[Route("api/v1/moderation/gray-zone-stories")]
public sealed class GrayZoneReviewController(GrayZoneWorkflowService workflow) : BaseApiController
{
    [HttpGet("{itemId:int}")]
    public async Task<IActionResult> Get(int itemId, CancellationToken ct) =>
        HandleResult(await workflow.GetAsync(GetCurrentUserId(), itemId, ct)).Result!;

    [HttpPost("{itemId:int}/decision")]
    public async Task<IActionResult> Decide(int itemId, GrayZoneDecisionRequest request, CancellationToken ct)
    {
        await workflow.DecideAsync(GetCurrentUserId(), itemId, request, ct);
        return Ok(new { success = true, message = "Đã ghi nhận quyết định kiểm duyệt." });
    }
}
