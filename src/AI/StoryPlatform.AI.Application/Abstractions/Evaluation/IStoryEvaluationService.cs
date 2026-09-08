using StoryPlatform.Contracts.AI.Models;

namespace StoryPlatform.AI.Application.Abstractions.Evaluation;

public interface IStoryEvaluationService
{
    Task<EvaluationResultDto> EvaluateAsync(
        StoryPackageDto story,
        GenerationConstraintsDto constraints,
        CancellationToken cancellationToken = default);
}
