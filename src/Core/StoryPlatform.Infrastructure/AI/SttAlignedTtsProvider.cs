using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StoryPlatform.Application.Features.MediaGeneration.Interfaces;
using StoryPlatform.Application.Features.MediaGeneration.Services;
using StoryPlatform.Application.Features.MediaStorage.Models;

namespace StoryPlatform.Infrastructure.AI;

/// <summary>
/// Bọc nhà cung cấp TTS không có mốc từng chữ (Gemini): nghe lại audio bằng STT rồi căn vào văn bản gốc.
/// Mọi lỗi (trừ hủy) đều trả audio gốc không mốc; audio không bao giờ bị chặn vì căn mốc.
/// </summary>
internal sealed class SttAlignedTtsProvider(
    ITtsProvider inner, IServiceScopeFactory scopes, IOptions<TtsServiceOptions> options,
    ILogger<SttAlignedTtsProvider> logger) : ITtsProvider
{
    private readonly TtsServiceOptions _options = options.Value;

    public string? ConfigurationFingerprint =>
        inner.ConfigurationFingerprint is { } fingerprint && _options.AlignWordTimingsWithStt ? fingerprint + "+stt1" : inner.ConfigurationFingerprint;

    public async Task<GeneratedMedia> GenerateAsync(string exactSceneText, CancellationToken cancellationToken = default)
    {
        var audio = await inner.GenerateAsync(exactSceneText, cancellationToken).ConfigureAwait(false);
        if (!_options.AlignWordTimingsWithStt
            || string.Equals(audio.GetMetadata("hasWordTimings"), "true", StringComparison.OrdinalIgnoreCase)
            || audio.Content.Length == 0 || audio.Content.Length > _options.MaxAlignedAudioBytes)
            return audio;

        var tokens = SsmlTokenizer.Tokenize(exactSceneText);
        if (!tokens.Any(t => t.IsWord)) return audio;

        try
        {
            using var scope = scopes.CreateScope();
            var aligner = scope.ServiceProvider.GetRequiredService<IAudioWordAligner>();
            var heard = await aligner.RecognizeWordsAsync(audio.Content, cancellationToken).ConfigureAwait(false);
            var json = WordTimingAligner.BuildWordTimingsJson(tokens, heard, _options.MinAlignmentCoverage);
            if (json is null)
            {
                logger.LogWarning("TTS word alignment rejected (coverage below {MinCoverage}); audio kept without timings",
                    _options.MinAlignmentCoverage);
                return audio;
            }

            var metadata = new Dictionary<string, string>(audio.Metadata ?? new Dictionary<string, string>())
            {
                ["hasWordTimings"] = "true",
                ["wordTimingsJson"] = json,
                ["timingSource"] = "stt"
            };
            return audio with { Metadata = metadata };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning("TTS word alignment failed Error={Error}; audio kept without timings", exception.GetType().Name);
            return audio;
        }
    }
}
