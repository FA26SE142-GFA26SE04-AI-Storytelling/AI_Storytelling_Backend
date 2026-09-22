using StoryPlatform.Application.Features.MediaGeneration.Services;
using StoryPlatform.Application.Features.MediaStorage.Models;

namespace StoryPlatform.Application.Features.MediaGeneration.Interfaces;

/// <summary>
/// Binary audio quality gate — validates that a generated audio asset has valid binary content
/// and that word-level timing marks are present and well-formed when expected.
///
/// The interface receives the list of expected timing marks produced by the SSML tokenizer
/// so the gate can compare timepoint count against mark count without re-tokenizing the text.
/// </summary>
public interface IAudioQualityGate
{
    AudioQualityGateResult Validate(GeneratedMedia audio, IReadOnlyList<TimingMark> expectedMarks);
}

/// <summary>
/// Result of an audio quality gate pass.
/// </summary>
public sealed record AudioQualityGateResult(bool IsPass, string? Reason = null)
{
    public static AudioQualityGateResult Pass() => new(true, null);
    public static AudioQualityGateResult Fail(string reason) => new(false, reason);
}
