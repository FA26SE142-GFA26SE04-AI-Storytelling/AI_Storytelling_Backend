using Microsoft.AspNetCore.Mvc;
using StoryPlatform.AI.Application.OutlineGeneration;
using StoryPlatform.Contracts.AI.Requests;
using StoryPlatform.Contracts.AI.Responses;

namespace StoryPlatform.AI.Api.Controllers;

[ApiController]
[Route("api/ai/outline")]
public sealed class OutlineGenerationController : ControllerBase
{
    [HttpPost]
    public Task<GenerateOutlineResponse> Generate(
        [FromBody] GenerateOutlineRequest request,
        [FromServices] GenerateOutlineHandler handler,
        CancellationToken cancellationToken) => handler.HandleAsync(request, cancellationToken);
}
