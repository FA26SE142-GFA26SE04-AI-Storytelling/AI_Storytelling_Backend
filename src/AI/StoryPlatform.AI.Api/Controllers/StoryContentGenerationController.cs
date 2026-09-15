using Microsoft.AspNetCore.Mvc;
using StoryPlatform.AI.Application.ContentGeneration;
using StoryPlatform.Contracts.AI.Requests;
using StoryPlatform.Contracts.AI.Responses;

namespace StoryPlatform.AI.Api.Controllers;

[ApiController]
[Route("api/ai/story-content")]
public sealed class StoryContentGenerationController : ControllerBase
{
    [HttpPost("content")]
    public Task<GenerateStoryContentResponse> GenerateContent([FromBody] GenerateStoryContentRequest request,
        [FromServices] GenerateStoryContentHandler handler, CancellationToken cancellationToken) => handler.HandleAsync(request, cancellationToken);

    [HttpPost("content/refine")]
    public Task<RefineStoryContentResponse> RefineContent([FromBody] RefineStoryContentRequest request,
        [FromServices] RefineStoryContentHandler handler, CancellationToken cancellationToken) => handler.HandleAsync(request, cancellationToken);

    [HttpPost("vocabulary")]
    public Task<GenerateVocabularyResponse> GenerateVocabulary([FromBody] GenerateVocabularyRequest request,
        [FromServices] GenerateVocabularyHandler handler, CancellationToken cancellationToken) => handler.HandleAsync(request, cancellationToken);

    [HttpPost("quiz")]
    public Task<GenerateQuizResponse> GenerateQuiz([FromBody] GenerateQuizRequest request,
        [FromServices] GenerateQuizHandler handler, CancellationToken cancellationToken) => handler.HandleAsync(request, cancellationToken);

    [HttpPost("discussion")]
    public Task<GenerateDiscussionResponse> GenerateDiscussion([FromBody] GenerateDiscussionRequest request,
        [FromServices] GenerateDiscussionHandler handler, CancellationToken cancellationToken) => handler.HandleAsync(request, cancellationToken);

    [HttpPost("content/safety")]
    public Task<EvaluateContentSafetyResponse> EvaluateContentSafety([FromBody] EvaluateContentSafetyRequest request,
        [FromServices] EvaluateContentSafetyHandler handler, CancellationToken cancellationToken) => handler.HandleAsync(request, cancellationToken);
}
