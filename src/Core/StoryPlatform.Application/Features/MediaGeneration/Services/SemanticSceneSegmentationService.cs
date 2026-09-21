using System;
using Microsoft.Extensions.Logging;
using StoryPlatform.Application.Features.MediaGeneration.Interfaces;
using StoryPlatform.Application.Features.MediaGeneration.Models;

namespace StoryPlatform.Application.Features.MediaGeneration.Services;

/// <summary>
/// Orchestrator that wraps primary LLM-based segmentation with a rule-based fallback.
/// Registered as the single implementation of <c>ISceneSegmentationProvider</c> so that
/// <c>MediaGenerationService</c> sees only one abstraction and is unaware of the underlying vendors.
/// </summary>
public sealed class SemanticSceneSegmentationService : ISceneSegmentationProvider
{
    private readonly ISemanticSceneSegmentationProvider _primary;
    private readonly IParagraphSceneSegmentationProvider _fallback;
    private readonly ILogger<SemanticSceneSegmentationService> _logger;

    public SemanticSceneSegmentationService(
        ISemanticSceneSegmentationProvider primary,
        IParagraphSceneSegmentationProvider fallback,
        ILogger<SemanticSceneSegmentationService> logger)
    {
        _primary = primary ?? throw new ArgumentNullException(nameof(primary));
        _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
        _logger = logger;
    }

    public async Task<IReadOnlyList<SceneSelection>> SegmentAsync(
        SceneSegmentationRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _primary.SegmentAsync(request, cancellationToken).ConfigureAwait(false);
            if (result.Count == 0)
            {
                _logger.LogWarning("Semantic segmenter returned empty result, falling back to paragraph");
                return await _fallback.SegmentAsync(request, cancellationToken).ConfigureAwait(false);
            }
            return result;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Semantic scene segmentation failed, falling back to paragraph-based");
            return await _fallback.SegmentAsync(request, cancellationToken).ConfigureAwait(false);
        }
    }
}
