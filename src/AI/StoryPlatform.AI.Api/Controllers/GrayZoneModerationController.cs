using Microsoft.AspNetCore.Mvc;
using StoryPlatform.AI.Application.Moderation;
using StoryPlatform.Contracts.AI.Moderation;

namespace StoryPlatform.AI.Api.Controllers;

[ApiController]
[Route("api/ai/moderation/gray-zone")]
public sealed class GrayZoneModerationController(EvaluateGrayZoneHandler handler) : ControllerBase
{
    [HttpPost("evaluate")]
    public Task<EvaluateGrayZoneResponse> Evaluate(EvaluateGrayZoneRequest request, CancellationToken ct) => handler.HandleAsync(request, ct);
}
