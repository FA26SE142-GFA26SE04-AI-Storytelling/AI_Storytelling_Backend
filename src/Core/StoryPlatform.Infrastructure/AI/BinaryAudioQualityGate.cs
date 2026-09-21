using StoryPlatform.Application.Features.MediaGeneration.Interfaces;
using StoryPlatform.Application.Features.MediaGeneration.Services;
using StoryPlatform.Application.Features.MediaStorage.Models;

namespace StoryPlatform.Infrastructure.AI;

/// <summary>
/// Rule-based audio quality gate.
///
/// Checks:
/// 1. Audio bytes are non-empty.
/// 2. Magic bytes match a known audio signature (WAV or MP3).
/// 3. When word timings are expected, the count of timepoints received from Cloud TTS
///    matches the count of &lt;mark/&gt; elements injected by the SSML tokenizer (±0 tolerance).
///
/// Does NOT call Gemini or any LLM — purely structural validation.
/// </summary>
public sealed class BinaryAudioQualityGate : IAudioQualityGate
{
    public AudioQualityGateResult Validate(GeneratedMedia audio, IReadOnlyList<TimingMark> expectedMarks)
    {
        // 1. Non-empty
        if (audio.Content is null || audio.Content.Length == 0)
            return AudioQualityGateResult.Fail("AUDIO_CONTENT_EMPTY");

        // 2. Magic bytes
        var magic = MagicByteValidators.ValidateAudio(audio.Content);
        if (!magic.IsValid)
            return AudioQualityGateResult.Fail($"AUDIO_MAGIC_BYTES_INVALID: {magic.Reason}");

        // 3. If word timings are expected, verify count matches (±0).
        if (expectedMarks.Count > 0)
        {
            var timingsJson = audio.GetMetadata("wordTimingsJson");
            if (string.IsNullOrEmpty(timingsJson))
                return AudioQualityGateResult.Fail("AUDIO_WORD_TIMINGS_MISSING");

            // Parse the timings to count timepoints.
            var count = CountTimepoints(timingsJson);
            if (count != expectedMarks.Count)
                return AudioQualityGateResult.Fail(
                    $"AUDIO_WORD_TIMINGS_COUNT_MISMATCH: expected {expectedMarks.Count}, got {count}");
        }

        return AudioQualityGateResult.Pass();
    }

    private static int CountTimepoints(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array
                ? doc.RootElement.GetArrayLength()
                : 0;
        }
        catch
        {
            return -1; // invalid JSON
        }
    }
}
