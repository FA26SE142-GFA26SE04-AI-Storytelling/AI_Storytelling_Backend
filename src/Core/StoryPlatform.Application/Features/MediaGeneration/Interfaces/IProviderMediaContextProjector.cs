using StoryPlatform.Application.Features.MediaGeneration.Models;

namespace StoryPlatform.Application.Features.MediaGeneration.Interfaces;

/// <summary>
/// Projects internal MediaContext.ContextJson into an allowlisted, typed DTO safe for media provider egress.
/// </summary>
public interface IProviderMediaContextProjector
{
    ProviderMediaContext Project(string contextJson);
}
