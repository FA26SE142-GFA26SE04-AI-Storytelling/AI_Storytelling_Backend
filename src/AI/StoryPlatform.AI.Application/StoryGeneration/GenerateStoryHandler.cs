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
        RequestGuard.Validate(request);
        var template = _promptProvider.GetActive(PromptType.Story, request.Language, request.AgeBand);
        var result = await GenerateAsync(PromptComposer.Compose(template, request), cancellationToken);
        var attempts = new List<GenerationAttemptMetadataDto> { result.ToAttempt("story_generation") };
        var promptVersions = new List<string> { template.Version };
        var promptVersion = template.Version;
        var story = EnrichStory(DeserializeStory(result.Content), request, promptVersion);
        var evaluation = await _evaluationService.EvaluateAsync(story, request.Constraints, cancellationToken);
        story = story with { ReadabilityMetrics = evaluation.ReadabilityMetrics };
        var refinementCount = 0;

        while (!evaluation.Passed && evaluation.SafetyPassed && refinementCount < _maxRefinementAttempts)
        {
            refinementCount++;
            var refineTemplate = _promptProvider.GetActive(PromptType.Refinement, request.Language, request.AgeBand);
            promptVersion = refineTemplate.Version;
            var refineInput = new RefineStoryRequest
            {
                RequestId = request.RequestId,
                Story = story,
                Language = request.Language,
                ReadingLevel = request.ReadingLevel,
                VocabularyLevel = request.VocabularyLevel,
                Reasons = evaluation.Issues,
                Constraints = request.Constraints
            };
            result = await GenerateAsync(PromptComposer.Compose(refineTemplate, refineInput), cancellationToken);
            attempts.Add(result.ToAttempt("story_refinement"));
            promptVersions.Add(refineTemplate.Version);
            story = EnrichStory(DeserializeStory(result.Content), request, refineTemplate.Version);
            evaluation = await _evaluationService.EvaluateAsync(story, request.Constraints, cancellationToken);
            story = story with { ReadabilityMetrics = evaluation.ReadabilityMetrics };
        }

        return new GenerateStoryResponse
        {
            RequestId = request.RequestId,
            GenerationId = Guid.NewGuid().ToString("N"),
            Story = story,
            Evaluation = evaluation,
            Metadata = result.ToMetadata(promptVersion, refinementCount, attempts, promptVersions)
        };
    }

    private Task<LlmGenerationResult> GenerateAsync(string prompt, CancellationToken cancellationToken) =>
        _llmClient.GenerateStructuredAsync(prompt, "story_package", GenerationSchemas.StoryPackage, cancellationToken);

    private static StoryPackageDto DeserializeStory(string content) =>
        JsonSerializer.Deserialize<StoryPackageDto>(content, JsonDefaults.Options)
        ?? throw new InvalidOperationException("The LLM returned an empty story package.");

    private static StoryPackageDto EnrichStory(
        StoryPackageDto story,
        GenerateStoryRequest request,
        string generationVersion) => story with
    {
        Source = "ai",
        AgeBand = request.AgeBand,
        ReadingLevel = request.ReadingLevel,
        VocabularyLevel = request.VocabularyLevel,
        Outline = request.Outline,
        GenerationVersion = generationVersion
    };
}
