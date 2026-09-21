using StoryPlatform.Application.Features.MediaGeneration.Models;

namespace StoryPlatform.Application.Features.MediaGeneration.Interfaces;

/// <summary>
/// Extracts richer semantic context from a story version for downstream media generation.
/// Implementations are Infrastructure (vendor-specific); this interface is Application-layer.
/// </summary>
public interface IMediaContextExtractor
{
    Task<ExtractedMediaContext> ExtractAsync(MediaContextBuildRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Rich semantic context extracted from a story version.
/// </summary>
public sealed record ExtractedMediaContext(
    string ContextJson,
    string? CharacterList,
    string? VisualStyleGuidance,
    string? SafetyConstraints);
