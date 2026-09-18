using StoryPlatform.Application.Features.MediaGeneration.Interfaces;
using StoryPlatform.Application.Features.MediaGeneration.Models;
using StoryPlatform.Application.Features.MediaGeneration;
using StoryPlatform.Application.Features.MediaStorage.Models;

namespace StoryPlatform.Infrastructure.AI;

/// <summary>
/// Fail-closed defaults. Deployments must replace these registrations with configured provider adapters.
/// </summary>
public sealed class UnavailableImageGenerationProvider : IImageGenerationProvider
{
    public Task<GeneratedMedia> GenerateAsync(
        SceneSpecification specification, CancellationToken cancellationToken = default) =>
        throw new PermanentMediaGenerationException("IMAGE_PROVIDER_NOT_CONFIGURED");
}

public sealed class UnavailableTtsProvider : ITtsProvider
{
    public Task<GeneratedMedia> GenerateAsync(string exactSceneText, CancellationToken cancellationToken = default) =>
        throw new PermanentMediaGenerationException("TTS_PROVIDER_NOT_CONFIGURED");
}

public sealed class FailClosedMediaEvaluator : IMediaAlignmentEvaluator, IMediaSafetyEvaluator
{
    public Task<MediaEvaluationResult> EvaluateAsync(
        SceneSpecification specification, GeneratedMedia illustration, CancellationToken cancellationToken = default) =>
        // Child-facing media is safety critical: an unavailable evaluator is an explicit failure,
        // never an uncertain result that another caller could accidentally accept.
        Task.FromResult(new MediaEvaluationResult(MediaEvaluationDecision.Fail, "MEDIA_EVALUATOR_NOT_CONFIGURED"));
}
