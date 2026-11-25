using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Application.Features.Moderation.Services;
using StoryPlatform.Api.Authorization;
using StoryPlatform.Application.Features.AccessControl;

namespace StoryPlatform.Api.Controllers;

[HasPermission(Permissions.Creator.Studio)]
[Route("api/v1/creator/stories")]
public sealed class CreatorModerationCheckController(GrayZoneContentCheckService checks) : BaseApiController
{
    public sealed record CheckRequest(int VersionId);

    [HttpPost("{storyId:int}/content/check")]
    public async Task<IActionResult> Check(int storyId, CheckRequest request, CancellationToken ct) =>
        Ok(StoryPlatform.Application.Common.Models.ApiResponse<GrayZoneContentCheckDto>.Ok(
            await checks.CheckAsync(GetCurrentUserId(), storyId, request.VersionId, ct)));
}
