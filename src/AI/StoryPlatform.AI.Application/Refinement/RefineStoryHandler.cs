using System.Text.Json;
using StoryPlatform.AI.Application.Abstractions.Evaluation;
using StoryPlatform.AI.Application.Abstractions.LLM;
using StoryPlatform.AI.Application.Abstractions.Prompting;
using StoryPlatform.AI.Application.Common;
using StoryPlatform.AI.Domain.Enums;
using StoryPlatform.Contracts.AI.Models;
using StoryPlatform.Contracts.AI.Requests;
using StoryPlatform.Contracts.AI.Responses;

namespace StoryPlatform.AI.Application.Refinement;

public sealed class RefineStoryHandler
{
    private readonly ILlmClient _llmClient;
    private readonly IPromptTemplateProvider _promptProvider;
    private readonly IStoryEvaluationService _evaluationService;

    public RefineStoryHandler(ILlmClient llmClient, IPromptTemplateProvider promptProvider, IStoryEvaluationService evaluationService)
    {
        _llmClient = llmClient;
        _promptProvider = promptProvider;
        _evaluationService = evaluationService;
    }

    public async Task<RefineStoryResponse> HandleAsync(RefineStoryRequest request, CancellationToken cancellationToken = default)
    {
        RequestGuard.Validate(request.RequestId, null, request.Constraints);
        var template = _promptProvider.GetActive(PromptType.Refinement, request.Language, request.Story.AgeBand);
        var result = await _llmClient.GenerateStructuredAsync(
            PromptComposer.Compose(template, request), "refined_story_package", GenerationSchemas.StoryPackage, cancellationToken);

        var story = JsonSerializer.Deserialize<StoryPackageDto>(result.Content, JsonDefaults.Options)
                    ?? throw new InvalidOperationException("The LLM returned an empty refined story package.");

        story = story with
        {
            Source = request.Story.Source,
            AgeBand = request.Story.AgeBand,
            ReadingLevel = string.IsNullOrWhiteSpace(request.ReadingLevel) ? request.Story.ReadingLevel : request.ReadingLevel,
            VocabularyLevel = string.IsNullOrWhiteSpace(request.VocabularyLevel) ? request.Story.VocabularyLevel : request.VocabularyLevel,
            Outline = request.Story.Outline,
            GenerationVersion = template.Version
        };
        var evaluation = await _evaluationService.EvaluateAsync(story, request.Constraints, cancellationToken);
        story = story with { ReadabilityMetrics = evaluation.ReadabilityMetrics };

        return new RefineStoryResponse
        {
            RequestId = request.RequestId,
            GenerationId = Guid.NewGuid().ToString("N"),
            Story = story,
            Evaluation = evaluation,
            Metadata = result.ToMetadata(template.Version, 1)
        };
    }
}
