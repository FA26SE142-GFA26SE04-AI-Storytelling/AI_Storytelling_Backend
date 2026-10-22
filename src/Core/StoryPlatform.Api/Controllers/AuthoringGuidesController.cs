using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.ParentAuthoring;

namespace StoryPlatform.Api.Controllers;

[ApiController]
[Authorize(Roles = "Parent")]
[Route("api/v1/authoring-guides")]
public sealed class AuthoringGuidesController(AuthoringGuideService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(string locale = "vi", string? purpose = null, string? group = null,
        int pageIndex = 1, int pageSize = 10, CancellationToken cancellationToken = default) =>
        Ok(ApiResponse<GuidePage>.Ok(await service.GetAsync(locale, purpose, group, pageIndex, pageSize, cancellationToken)));

    [HttpPost("{guideId}/render")]
    public async Task<IActionResult> Render(string guideId, RenderGuideRequest request, CancellationToken cancellationToken) =>
        Ok(ApiResponse<RenderGuideResult>.Ok(await service.RenderAsync(guideId, request, cancellationToken)));
}
