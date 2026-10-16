using Microsoft.AspNetCore.Mvc;
using StoryPlatform.AI.Application.CreatorAssistance;
using StoryPlatform.Contracts.AI.CreatorAssistance;

namespace StoryPlatform.AI.Api.Controllers;

[ApiController]
[Route("api/ai/creator-assistance")]
[RequestSizeLimit(1_000_000)]
public sealed class CreatorAssistanceController(CreatorAssistanceHandler handler) : ControllerBase
{
    [HttpPost]
    public Task<AssistanceAiResult> Generate(AssistanceAiRequest request, CancellationToken ct) => handler.HandleAsync(request, ct);
}
