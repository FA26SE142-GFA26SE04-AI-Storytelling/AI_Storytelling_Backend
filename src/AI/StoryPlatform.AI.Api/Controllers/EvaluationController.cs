using Microsoft.AspNetCore.Mvc;
using StoryPlatform.AI.Application.Evaluation;
using StoryPlatform.Contracts.AI.Requests;
using StoryPlatform.Contracts.AI.Responses;

namespace StoryPlatform.AI.Api.Controllers;

[ApiController]
[Route("api/ai/evaluate")]
public sealed class EvaluationController : ControllerBase
{
    [HttpPost]
    public Task<EvaluateStoryResponse> Evaluate(
        [FromBody] EvaluateStoryRequest request,
        [FromServices] EvaluateStoryHandler handler,
        CancellationToken cancellationToken) => handler.HandleAsync(request, cancellationToken);
}
