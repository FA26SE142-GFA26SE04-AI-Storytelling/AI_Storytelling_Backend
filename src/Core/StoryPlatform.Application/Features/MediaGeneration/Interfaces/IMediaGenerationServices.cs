using StoryPlatform.Application.Features.MediaGeneration.Models;
using StoryPlatform.Application.Features.MediaStorage.Models;

namespace StoryPlatform.Application.Features.MediaGeneration.Interfaces;

public interface IMediaContextBuilder
{
    string Build(MediaContextBuildRequest request);
}

public interface IStoryBlockParser
{
    IReadOnlyList<StoryTextBlock> Parse(string content);
}

public interface ISceneSegmentationProvider
{
    Task<IReadOnlyList<SceneSelection>> SegmentAsync(SceneSegmentationRequest request, CancellationToken cancellationToken = default);
}

public interface ISceneCoverageValidator
{
    IReadOnlyList<ValidatedScene> ValidateAndAssemble(
        string canonicalContent,
        IReadOnlyList<StoryTextBlock> blocks,
        IReadOnlyList<SceneSelection> selections);
}

public interface ISceneSpecificationBuilder
{
    SceneSpecification Build(int storyVersionId, int storySceneId, int sceneIndex, string sceneText,
        string? visualDescription, string mediaContextJson);
}

public interface IIllustrationBeatPlanner
{
    Task<IReadOnlyList<IllustrationBeatSelection>> PlanAsync(
        IllustrationBeatPlanRequest request, CancellationToken cancellationToken = default);
}

public interface IImageGenerationProvider
{
    Task<GeneratedMedia> GenerateAsync(SceneSpecification specification, CancellationToken cancellationToken = default);
}

public interface ITtsProvider
{
    Task<GeneratedMedia> GenerateAsync(string exactSceneText, CancellationToken cancellationToken = default);
}

public interface IMediaAlignmentEvaluator
{
    Task<MediaEvaluationResult> EvaluateAsync(
        SceneSpecification specification, GeneratedMedia illustration, CancellationToken cancellationToken = default);
}

public interface IMediaSafetyEvaluator
{
    Task<MediaEvaluationResult> EvaluateAsync(
        SceneSpecification specification, GeneratedMedia illustration, CancellationToken cancellationToken = default);
}

public interface IMediaGenerationService
{
    Task<MediaGenerationProgress> GetProgressAsync(int userId, int storyId, CancellationToken cancellationToken = default);
    Task<StoryMediaPackage> GetPackageAsync(int userId, int storyId, CancellationToken cancellationToken = default);
    Task<MediaGenerationProgress> RetryAsync(int userId, int storyId, CancellationToken cancellationToken = default);
    Task RegenerateIllustrationBeatAsync(int userId, int storyId, int beatId, RegenerateMediaRequest request, CancellationToken cancellationToken = default);
    Task RegenerateSegmentAudioAsync(int userId, int storyId, int segmentId, RegenerateMediaRequest request, CancellationToken cancellationToken = default);
    Task<MediaGenerationProgress> ConfirmAsync(int userId, int storyId, ConfirmMediaPackageRequest request, CancellationToken cancellationToken = default);
    Task<MediaGenerationProgress> KeepCurrentAsync(int userId, int storyId, int candidateAssetId, KeepCurrentMediaRequest request, CancellationToken cancellationToken = default);
}

public interface IMediaReadinessService
{
    Task<MediaReadinessResult> CheckAsync(int storyVersionId, int? expectedSceneCount,
        CancellationToken cancellationToken = default);
}

public interface IMediaGenerationJobProcessor
{
    Task<MediaJobProcessResult> ProcessNextAsync(CancellationToken cancellationToken = default);
    Task<MediaJobProcessResult> ProcessBranchNextAsync(StoryPlatform.Domain.Enums.MediaType type, CancellationToken cancellationToken = default);
}

public interface IMediaGenerationJobFailureFinalizer
{
    Task MarkFailedAsync(int jobId, string expectedConcurrencyToken, string errorCode,
        CancellationToken cancellationToken = default);
}
