using System.Diagnostics;
using StoryPlatform.AI.Application.Abstractions.Evaluation;
using StoryPlatform.AI.Application.Common;
using StoryPlatform.Contracts.AI.Models;
using StoryPlatform.Contracts.AI.Requests;
using StoryPlatform.Contracts.AI.Responses;

namespace StoryPlatform.AI.Application.Evaluation;

public sealed class EvaluateStoryHandler
{
    private readonly IStoryEvaluationService _evaluationService;

    public EvaluateStoryHandler(IStoryEvaluationService evaluationService)
    {
        _evaluationService = evaluationService;
    }

    public async Task<EvaluateStoryResponse> HandleAsync(EvaluateStoryRequest request, CancellationToken cancellationToken = default)
    {
        RequestGuard.Validate(request.RequestId, null, request.Constraints);
        var stopwatch = Stopwatch.StartNew();
        var evaluation = await _evaluationService.EvaluateAsync(request.Story, request.Constraints, cancellationToken);
        stopwatch.Stop();

        return new EvaluateStoryResponse
        {
            RequestId = request.RequestId,
            GenerationId = Guid.NewGuid().ToString("N"),
            Evaluation = evaluation,
            Metadata = new GenerationMetadataDto
            {
                ModelProvider = "deterministic",
                Model = "rule-based-evaluator",
                ModelVersion = "1.0",
                PromptVersion = "none",
                LatencyMs = stopwatch.ElapsedMilliseconds
            }
        };
    }
}
