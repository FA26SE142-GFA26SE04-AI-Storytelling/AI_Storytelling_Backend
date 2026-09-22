using StoryPlatform.Application.Features.MediaGeneration.Models;

namespace StoryPlatform.Application.Features.MediaGeneration.Interfaces;

/// <summary>
/// LLM-powered semantic scene segmentation — splits story content into scenes using AI reasoning.
/// Application-layer interface: vendor name must NOT appear here.
/// </summary>
public interface ISemanticSceneSegmentationProvider
{
    Task<IReadOnlyList<SceneSelection>> SegmentAsync(
        SceneSegmentationRequest request, CancellationToken cancellationToken = default);
}
