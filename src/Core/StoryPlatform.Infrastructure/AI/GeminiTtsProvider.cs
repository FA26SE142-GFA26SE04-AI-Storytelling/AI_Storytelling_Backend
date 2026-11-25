using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StoryPlatform.Application.Features.MediaGeneration;
using StoryPlatform.Application.Features.MediaGeneration.Interfaces;
using StoryPlatform.Application.Features.MediaGeneration.Services;
using StoryPlatform.Application.Features.MediaStorage.Models;

namespace StoryPlatform.Infrastructure.AI;

/// <summary>
/// Gemini Multimodal Text-to-Speech provider (Single TTS Mode).
/// Routes to Vertex AI (OAuth2 Bearer token) using gemini-2.5-flash-tts with AUDIO response modality.
/// Generates expressive natural storytelling audio without SSML dependency.
/// </summary>
public sealed class GeminiTtsProvider : ITtsProvider
{
    private const string OperationName = "TtsGeneration";
    private const string ProviderName = "GeminiTTS";

    public string ConfigurationFingerprint => _options.Fingerprint(nameof(GeminiTtsProvider));
    private readonly HttpClient _httpClient;
    private readonly GeminiOptions _gemini;
    private readonly VertexOptions _vertex;
    private readonly TtsServiceOptions _options;
    private readonly ILogger<GeminiTtsProvider> _logger;
    private readonly IMediaOutboundPrivacyGuard _privacyGuard;

    public GeminiTtsProvider(
        HttpClient httpClient,
        IOptions<GeminiOptions> geminiOptions,
        IOptions<VertexOptions> vertexOptions,
        IOptions<TtsServiceOptions> options,
        ILogger<GeminiTtsProvider> logger,
        IMediaOutboundPrivacyGuard? privacyGuard = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _gemini = geminiOptions?.Value ?? throw new ArgumentNullException(nameof(geminiOptions));
        _vertex = vertexOptions?.Value ?? throw new ArgumentNullException(nameof(vertexOptions));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _privacyGuard = privacyGuard ?? new DeterministicMediaOutboundPrivacyGuard();

        // Use the configured TTS budget, including when HttpClient's default (100s) is shorter.
        _httpClient.Timeout = TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 1, 600));
    }

    public async Task<GeneratedMedia> GenerateAsync(string exactSceneText, CancellationToken cancellationToken = default)
    {
        var elapsed = Stopwatch.StartNew();
        MediaProviderDiagnostics.LogStarted(
            _logger, OperationName, ProviderName, _options.Model, exactSceneText?.Length ?? 0);

        int? httpStatus = null;
        string? providerRequestId = null;
        double? retryAfterSeconds = null;

        try
        {
            var (audio, status, reqId, retryAfter, usage) =
                await GenerateCoreAsync(exactSceneText!, cancellationToken).ConfigureAwait(false);
            httpStatus = status;
            providerRequestId = reqId;
            retryAfterSeconds = retryAfter;
            MediaProviderDiagnostics.LogCompleted(
                _logger, OperationName, ProviderName, _options.Model, elapsed.Elapsed.TotalMilliseconds,
                status, reqId, audio.Content.Length, audio.MimeType, usage);
            return audio;
        }
        catch (OperationCanceledException)
        {
            MediaProviderDiagnostics.LogCancelled(
                _logger, OperationName, ProviderName, _options.Model, elapsed.Elapsed.TotalMilliseconds,
                cancellationToken.IsCancellationRequested);
            throw;
        }
        catch (MediaProviderPrivacyException ex)
        {
            MediaProviderDiagnostics.LogFailed(
                _logger, OperationName, ProviderName, _options.Model, elapsed.Elapsed.TotalMilliseconds,
                httpStatus, ex.ErrorCode, retryAfterSeconds, providerRequestId, ex.GetType().Name);
            throw;
        }
        catch (Exception error)
        {
            var code = error is PermanentMediaGenerationException permanent ? permanent.ErrorCode :
                error is TransientMediaGenerationException transient ? transient.ErrorCode : "TTS_PROVIDER_FAILED";
            var status = error is TransientMediaGenerationException t && t.InnerException is HttpRequestException h && h.StatusCode is { } sc
                ? (int?)sc
                : httpStatus;
            var retryAfter = error is TransientMediaGenerationException tr && tr.RetryAfter is { } ra
                ? Math.Round(Math.Max(0, ra.TotalSeconds), 3)
                : retryAfterSeconds;

            MediaProviderDiagnostics.LogFailed(
                _logger, OperationName, ProviderName, _options.Model, elapsed.Elapsed.TotalMilliseconds,
                status, code, retryAfter, providerRequestId, error.GetType().Name);
            throw;
        }
    }

    private async Task<(GeneratedMedia Audio, int HttpStatus, string? ProviderRequestId, double? RetryAfterSeconds, ProviderTokenUsage Usage)> GenerateCoreAsync(
        string exactSceneText, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(exactSceneText))
            throw new PermanentMediaGenerationException("TTS_EMPTY_TEXT");
        if (exactSceneText.Length > _options.MaxTextLength)
            throw new PermanentMediaGenerationException("TTS_TEXT_TOO_LONG");

        _privacyGuard.EnsureSafeForProvider(OperationName, exactSceneText);

        if (!_vertex.UseVertex && string.IsNullOrWhiteSpace(_gemini.ApiKey))
            throw new PermanentMediaGenerationException("TTS_PROVIDER_NOT_CONFIGURED");

        var body = BuildRequestBody(exactSceneText, _options.VoiceName, _options.LanguageCode);
        var payloadJson = JsonSerializer.Serialize(body);
        using var request = BuildRequest(payloadJson);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var httpStatus = (int)response.StatusCode;
        var providerRequestId = MediaProviderDiagnostics.ExtractAllowlistedRequestId(response);
        var retryAfterSeconds = MediaProviderDiagnostics.GetRetryAfterSeconds(response);

        if (!response.IsSuccessStatusCode)
        {
            _ = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            var errorCode = $"GEMINI_TTS_HTTP_{httpStatus}";
            if (IsTransient(response.StatusCode))
            {
                throw new TransientMediaGenerationException(
                    errorCode,
                    new HttpRequestException(errorCode, null, response.StatusCode),
                    MediaProviderDiagnostics.GetRetryAfterDelta(response));
            }
            throw new PermanentMediaGenerationException(errorCode);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var (bytes, detectedMime, usage) = await ExtractFirstInlineAudioAsync(stream, cancellationToken).ConfigureAwait(false);
        if (PcmAudioConverter.IsLinear16(detectedMime))
        {
            bytes = PcmAudioConverter.WrapAsWav(bytes, detectedMime);
            detectedMime = MagicByteValidators.WavMime;
        }

        var validation = MagicByteValidators.ValidateAudio(bytes);
        if (!validation.IsValid)
            throw new PermanentMediaGenerationException("AUDIO_QUALITY_GATE_FAILED");

        var mimeType = detectedMime ?? validation.DetectedMime ?? "audio/wav";

        var audio = new GeneratedMedia(bytes, mimeType, new Dictionary<string, string>
        {
            ["provider"] = ProviderName,
            ["model"] = _options.Model,
            ["voice"] = _options.VoiceName,
            ["hasWordTimings"] = "false"
        });
        return (audio, httpStatus, providerRequestId, retryAfterSeconds, usage);
    }

    private HttpRequestMessage BuildRequest(string payloadJson)
    {
        var url = VertexUrlResolver.Resolve(_vertex, _gemini, _options.Model);
        var message = new HttpRequestMessage(HttpMethod.Post, url);
        if (!_vertex.UseVertex)
        {
            message.Headers.Add("x-goog-api-key", _gemini.ApiKey);
        }
        message.Content = new StringContent(payloadJson, Encoding.UTF8, "application/json");
        return message;
    }

    private static object BuildRequestBody(string text, string voiceName, string languageCode) => new
    {
        contents = new[]
        {
            new
            {
                role = "user",
                parts = new[]
                {
                    new { text }
                }
            }
        },
        generationConfig = new
        {
            responseModalities = new[] { "AUDIO" },
            speechConfig = new
            {
                languageCode,
                voiceConfig = new
                {
                    prebuiltVoiceConfig = new
                    {
                        voiceName
                    }
                }
            }
        }
    };

    private static async Task<(byte[] bytes, string? mimeHint, ProviderTokenUsage usage)> ExtractFirstInlineAudioAsync(
        Stream responseBody, CancellationToken cancellationToken)
    {
        using var ms = new MemoryStream();
        await responseBody.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
        var json = Encoding.UTF8.GetString(ms.ToArray());
        var usage = MediaProviderDiagnostics.ExtractUsageMetadata(json);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("candidates", out var candidates) || candidates.ValueKind != JsonValueKind.Array || candidates.GetArrayLength() == 0)
            throw new PermanentMediaGenerationException("GEMINI_TTS_NO_CANDIDATES");

        var parts = candidates[0].GetProperty("content").GetProperty("parts");
        foreach (var part in parts.EnumerateArray())
        {
            if (part.TryGetProperty("inlineData", out var inline))
            {
                var b64 = inline.GetProperty("data").GetString()
                          ?? throw new PermanentMediaGenerationException("GEMINI_TTS_EMPTY_DATA");
                var mime = inline.TryGetProperty("mimeType", out var m) ? m.GetString() : null;
                return (Convert.FromBase64String(b64), mime, usage);
            }
        }
        throw new PermanentMediaGenerationException("GEMINI_TTS_NO_INLINE_DATA");
    }

    private static bool IsTransient(System.Net.HttpStatusCode code) =>
        code is System.Net.HttpStatusCode.TooManyRequests or System.Net.HttpStatusCode.RequestTimeout or
        System.Net.HttpStatusCode.ServiceUnavailable || (int)code >= 500;
}
