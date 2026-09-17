using StoryPlatform.Application.Features.MediaGeneration.Interfaces;
using StoryPlatform.Application.Features.MediaGeneration.Models;

namespace StoryPlatform.Application.Features.MediaGeneration.Services;

/// <summary>
/// Safe default segmenter. It selects canonical block IDs only; an AI adapter can replace it without
/// changing validation or persistence.
/// </summary>
public sealed class ParagraphSceneSegmentationProvider : ISceneSegmentationProvider
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
