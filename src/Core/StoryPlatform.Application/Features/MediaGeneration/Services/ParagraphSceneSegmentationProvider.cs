using StoryPlatform.Application.Features.MediaGeneration.Interfaces;
using StoryPlatform.Application.Features.MediaGeneration.Models;

namespace StoryPlatform.Application.Features.MediaGeneration.Services;

/// <summary>
/// Safe default segmenter. It selects canonical block IDs only; an AI adapter can replace it without
/// changing validation or persistence.
///
/// Implements both <c>ISceneSegmentationProvider</c> (orchestrator-facing) and
/// <c>IParagraphSceneSegmentationProvider</c> (Phase 5 vendor-neutral fallback interface) using
/// a single public method that satisfies both contracts.
/// </summary>
public sealed class ParagraphSceneSegmentationProvider : ISceneSegmentationProvider, IParagraphSceneSegmentationProvider
{
    public Task<IReadOnlyList<SceneSelection>> SegmentAsync(
        SceneSegmentationRequest request, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<SceneSelection> result = request.Blocks
            .Select((block, index) => new SceneSelection(index, new[] { block.BlockId }))
            .ToArray();
        return Task.FromResult(result);
    }
}
