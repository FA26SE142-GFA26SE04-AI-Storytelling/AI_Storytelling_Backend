namespace StoryPlatform.Application.Features.MediaGeneration.Interfaces;

/// <summary>
/// Deterministic pre-dispatch privacy guard for outbound text sent to external media providers.
/// </summary>
public interface IMediaOutboundPrivacyGuard
{
    void EnsureSafeForProvider(string channel, params string?[] textSegments);
    void EnsureSafeForProvider(string channel, IEnumerable<string?> textSegments);
}
