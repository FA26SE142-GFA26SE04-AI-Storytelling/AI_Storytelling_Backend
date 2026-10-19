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
    public IActionResult Get(string locale = "vi", string? purpose = null, string? group = null, int pageIndex = 1, int pageSize = 10) =>
        Ok(ApiResponse<GuidePage>.Ok(service.Get(locale, purpose, group, pageIndex, pageSize)));

    [HttpPost("{guideId}/render")]
    public IActionResult Render(string guideId, RenderGuideRequest request) =>
        Ok(ApiResponse<RenderGuideResult>.Ok(service.Render(guideId, request)));
}
