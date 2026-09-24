using System;
using System.Collections.Generic;
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
using StoryPlatform.Application.Features.MediaStorage.Models;

namespace StoryPlatform.Infrastructure.AI;

/// <summary>
/// Gemini Multimodal Text-to-Speech provider (Single TTS Mode).
/// Routes to Vertex AI (OAuth2 Bearer token) using gemini-2.5-flash-tts with AUDIO response modality.
/// Generates expressive natural storytelling audio without SSML dependency.
/// </summary>
public sealed class GeminiTtsProvider : ITtsProvider
{
    private readonly HttpClient _httpClient;
    private readonly GeminiOptions _gemini;
    private readonly VertexOptions _vertex;
    private readonly TtsServiceOptions _options;
    private readonly ILogger<GeminiTtsProvider> _logger;

    public GeminiTtsProvider(
        HttpClient httpClient,
        IOptions<GeminiOptions> geminiOptions,
        IOptions<VertexOptions> vertexOptions,
        IOptions<TtsServiceOptions> options,
        ILogger<GeminiTtsProvider> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _gemini = geminiOptions?.Value ?? throw new ArgumentNullException(nameof(geminiOptions));
        _vertex = vertexOptions?.Value ?? throw new ArgumentNullException(nameof(vertexOptions));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (_httpClient.Timeout == Timeout.InfiniteTimeSpan || _httpClient.Timeout > TimeSpan.FromSeconds(_options.TimeoutSeconds))
            _httpClient.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
    }

    public async Task<GeneratedMedia> GenerateAsync(string exactSceneText, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(exactSceneText))
            throw new PermanentMediaGenerationException("TTS_EMPTY_TEXT");
        if (exactSceneText.Length > _options.MaxTextLength)
            throw new PermanentMediaGenerationException("TTS_TEXT_TOO_LONG");

        if (!_vertex.UseVertex && string.IsNullOrWhiteSpace(_gemini.ApiKey))
            throw new PermanentMediaGenerationException("TTS_PROVIDER_NOT_CONFIGURED");

        var body = BuildRequestBody(exactSceneText, _options.VoiceName, _options.LanguageCode);
        using var request = BuildRequest(body);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (IsTransient(response.StatusCode))
                throw new HttpRequestException($"GEMINI_TTS_TRANSIENT_HTTP_{(int)response.StatusCode}: {Truncate(error, 200)}");
            throw new PermanentMediaGenerationException($"GEMINI_TTS_HTTP_{(int)response.StatusCode}: {Truncate(error, 200)}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var (bytes, detectedMime) = await ExtractFirstInlineAudioAsync(stream, cancellationToken).ConfigureAwait(false);
        if (PcmAudioConverter.IsLinear16(detectedMime))
        {
            bytes = PcmAudioConverter.WrapAsWav(bytes, detectedMime);
            detectedMime = MagicByteValidators.WavMime;
        }

        var validation = MagicByteValidators.ValidateAudio(bytes);
        if (!validation.IsValid)
            throw new PermanentMediaGenerationException($"GEMINI_TTS_VALIDATION_FAILED: {validation.Reason}");

        var mimeType = detectedMime ?? validation.DetectedMime ?? "audio/wav";

        return new GeneratedMedia(bytes, mimeType, new Dictionary<string, string>
        {
            ["provider"] = "GeminiTTS",
            ["model"] = _options.Model,
            ["voice"] = _options.VoiceName,
            ["hasWordTimings"] = "false"
        });
    }

    private HttpRequestMessage BuildRequest(object body)
    {
        var url = VertexUrlResolver.Resolve(_vertex, _gemini, _options.Model);
        var message = new HttpRequestMessage(HttpMethod.Post, url);
        if (!_vertex.UseVertex)
        {
            message.Headers.Add("x-goog-api-key", _gemini.ApiKey);
        }
        message.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
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

    private static async Task<(byte[] bytes, string? mimeHint)> ExtractFirstInlineAudioAsync(
        Stream responseBody, CancellationToken cancellationToken)
    {
        using var ms = new MemoryStream();
        await responseBody.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
        var json = Encoding.UTF8.GetString(ms.ToArray());
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
                return (Convert.FromBase64String(b64), mime);
            }
        }
        throw new PermanentMediaGenerationException("GEMINI_TTS_NO_INLINE_DATA");
    }

    private static string Truncate(string value, int max) =>
        string.IsNullOrEmpty(value) ? string.Empty :
        value.Length <= max ? value : value[..max];

    private static bool IsTransient(System.Net.HttpStatusCode code) =>
        code is System.Net.HttpStatusCode.TooManyRequests or System.Net.HttpStatusCode.RequestTimeout or
        System.Net.HttpStatusCode.ServiceUnavailable || (int)code >= 500;
}
