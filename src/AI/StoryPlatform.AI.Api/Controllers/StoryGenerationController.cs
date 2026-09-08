using Microsoft.AspNetCore.Mvc;
using StoryPlatform.AI.Application.StoryGeneration;
using StoryPlatform.Contracts.AI.Requests;
using StoryPlatform.Contracts.AI.Responses;

namespace StoryPlatform.AI.Api.Controllers;

[ApiController]
[Route("api/ai/story")]
public sealed class StoryGenerationController : ControllerBase
{
    [HttpPost]
    public Task<GenerateStoryResponse> Generate(
        [FromBody] GenerateStoryRequest request,
        [FromServices] GenerateStoryHandler handler,
        CancellationToken cancellationToken) => handler.HandleAsync(request, cancellationToken);
}
