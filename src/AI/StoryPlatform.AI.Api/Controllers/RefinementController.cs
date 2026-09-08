using Microsoft.AspNetCore.Mvc;
using StoryPlatform.AI.Application.Refinement;
using StoryPlatform.Contracts.AI.Requests;
using StoryPlatform.Contracts.AI.Responses;

namespace StoryPlatform.AI.Api.Controllers;

[ApiController]
[Route("api/ai/refine")]
public sealed class RefinementController : ControllerBase
{
    [HttpPost]
    public Task<RefineStoryResponse> Refine(
        [FromBody] RefineStoryRequest request,
        [FromServices] RefineStoryHandler handler,
        CancellationToken cancellationToken) => handler.HandleAsync(request, cancellationToken);
}
