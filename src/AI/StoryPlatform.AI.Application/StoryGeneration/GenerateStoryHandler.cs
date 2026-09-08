using System.Text.Json;
using Microsoft.Extensions.Options;
using StoryPlatform.AI.Application.Abstractions.Evaluation;
using StoryPlatform.AI.Application.Abstractions.LLM;
using StoryPlatform.AI.Application.Abstractions.Prompting;
using StoryPlatform.AI.Application.Common;
using StoryPlatform.AI.Domain.Enums;
using StoryPlatform.AI.Domain.Generation;
using StoryPlatform.Contracts.AI.Models;
using StoryPlatform.Contracts.AI.Requests;
using StoryPlatform.Contracts.AI.Responses;

namespace StoryPlatform.AI.Application.StoryGeneration;

public sealed class GenerateStoryHandler
{
    private readonly ILlmClient _llmClient;
    private readonly IPromptTemplateProvider _promptProvider;
    private readonly IStoryEvaluationService _evaluationService;
    private readonly int _maxRefinementAttempts;

    public GenerateStoryHandler(
        ILlmClient llmClient,
        IPromptTemplateProvider promptProvider,
        IStoryEvaluationService evaluationService,
        IOptions<GenerateStoryOptions> options)
    {
        _llmClient = llmClient;
        _promptProvider = promptProvider;
        _evaluationService = evaluationService;
        _maxRefinementAttempts = Math.Clamp(options.Value.MaxRefinementAttempts, 0, 2);
    }

    public async Task<GenerateStoryResponse> HandleAsync(GenerateStoryRequest request, CancellationToken cancellationToken = default)
    {
        RequestGuard.Validate(request.RequestId, request.StoryParameters, request.Constraints);
        var template = _promptProvider.GetActive(PromptType.Story, request.Language, request.AgeBand);
        var result = await GenerateAsync(PromptComposer.Compose(template, request), cancellationToken);
        var promptVersion = template.Version;
        var story = DeserializeStory(result.Content);
        var evaluation = await _evaluationService.EvaluateAsync(story, request.Constraints, cancellationToken);
        var refinementCount = 0;

        while (!evaluation.Passed && refinementCount < _maxRefinementAttempts)
        {
            refinementCount++;
            var refineTemplate = _promptProvider.GetActive(PromptType.Refinement, request.Language, request.AgeBand);
            promptVersion = refineTemplate.Version;
            var refineInput = new RefineStoryRequest
            {
                RequestId = request.RequestId,
                Story = story,
                Reasons = evaluation.Issues,
                Constraints = request.Constraints
            };
            result = await GenerateAsync(PromptComposer.Compose(refineTemplate, refineInput), cancellationToken);
            story = DeserializeStory(result.Content);
            evaluation = await _evaluationService.EvaluateAsync(story, request.Constraints, cancellationToken);
        }

        return new GenerateStoryResponse
        {
            RequestId = request.RequestId,
            GenerationId = Guid.NewGuid().ToString("N"),
            Story = story,
            Evaluation = evaluation,
            Metadata = result.ToMetadata(promptVersion, refinementCount)
        };
    }

    private Task<LlmGenerationResult> GenerateAsync(string prompt, CancellationToken cancellationToken) =>
        _llmClient.GenerateStructuredAsync(prompt, "story_package", GenerationSchemas.StoryPackage, cancellationToken);

    private static StoryPackageDto DeserializeStory(string content) =>
        JsonSerializer.Deserialize<StoryPackageDto>(content, JsonDefaults.Options)
        ?? throw new InvalidOperationException("The LLM returned an empty story package.");
}
