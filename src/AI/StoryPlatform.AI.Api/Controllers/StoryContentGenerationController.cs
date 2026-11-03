using Microsoft.AspNetCore.Mvc;
using StoryPlatform.AI.Application.ContentGeneration;
using StoryPlatform.Contracts.AI.Requests;
using StoryPlatform.Contracts.AI.Responses;

namespace StoryPlatform.AI.Api.Controllers;

[ApiController]
[Route("api/ai/story-content")]
public sealed class StoryContentGenerationController : ControllerBase
{
    [HttpGet("listening/snapshot")]
    public StoryPlatform.Contracts.AI.Models.ListeningPromptSnapshotDto ListeningSnapshot(
        [FromQuery] string interactionType, [FromQuery] string ageBand, [FromQuery] string language,
        [FromServices] GenerateListeningInteractionHandler handler) => handler.Snapshot(interactionType, ageBand, language);

    [HttpPost("listening")]
    public Task<GenerateListeningInteractionResponse> GenerateListening([FromBody] GenerateListeningInteractionRequest request,
        [FromServices] GenerateListeningInteractionHandler handler, CancellationToken ct) => handler.HandleAsync(request, ct);

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

    [HttpPost("content/safety")]
    public Task<EvaluateContentSafetyResponse> EvaluateContentSafety([FromBody] EvaluateContentSafetyRequest request,
        [FromServices] EvaluateContentSafetyHandler handler, CancellationToken cancellationToken) => handler.HandleAsync(request, cancellationToken);
}
