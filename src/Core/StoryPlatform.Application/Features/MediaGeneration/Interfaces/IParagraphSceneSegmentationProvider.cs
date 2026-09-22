using StoryPlatform.Application.Features.MediaGeneration.Models;

namespace StoryPlatform.Application.Features.MediaGeneration.Interfaces;

/// <summary>
/// Fallback rule-based paragraph segmentation (1 paragraph = 1 scene).
/// Application-layer interface: used when LLM segmentation is unavailable or fails.
/// </summary>
public interface IParagraphSceneSegmentationProvider
{
    Task<IReadOnlyList<SceneSelection>> SegmentAsync(
        SceneSegmentationRequest request, CancellationToken cancellationToken = default);
}
