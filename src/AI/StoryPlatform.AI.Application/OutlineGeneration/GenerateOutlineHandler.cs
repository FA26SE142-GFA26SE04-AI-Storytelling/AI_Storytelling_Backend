using System.Text.Json;
using StoryPlatform.AI.Application.Abstractions.LLM;
using StoryPlatform.AI.Application.Abstractions.Prompting;
using StoryPlatform.AI.Application.Common;
using StoryPlatform.AI.Domain.Enums;
using StoryPlatform.Contracts.AI.Models;
using StoryPlatform.Contracts.AI.Requests;
using StoryPlatform.Contracts.AI.Responses;

namespace StoryPlatform.AI.Application.OutlineGeneration;

public sealed class GenerateOutlineHandler
{
    private readonly ILlmClient _llmClient;
    private readonly IPromptTemplateProvider _promptProvider;

    public GenerateOutlineHandler(ILlmClient llmClient, IPromptTemplateProvider promptProvider)
    {
        _llmClient = llmClient;
        _promptProvider = promptProvider;
    }

    public async Task<GenerateOutlineResponse> HandleAsync(GenerateOutlineRequest request, CancellationToken cancellationToken = default)
    {
        RequestGuard.Validate(request);
        var template = _promptProvider.GetActive(PromptType.Outline, request.Language, request.AgeBand);
        var prompt = PromptComposer.Compose(template, request);
        var result = await _llmClient.GenerateStructuredAsync(prompt, "story_outline", GenerationSchemas.Outline, cancellationToken);
        var payload = JsonSerializer.Deserialize<OutlinePayload>(result.Content, JsonDefaults.Options)
                      ?? throw new InvalidOperationException("The LLM returned an empty outline payload.");

        return new GenerateOutlineResponse
        {
            RequestId = request.RequestId,
            GenerationId = Guid.NewGuid().ToString("N"),
            Title = payload.Title,
            Outline = payload.Outline,
            Metadata = result.ToMetadata(template.Version)
        };
    }

    private sealed record OutlinePayload(string Title, StoryOutlineDto Outline);
}
