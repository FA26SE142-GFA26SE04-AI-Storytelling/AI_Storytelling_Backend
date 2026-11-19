using System;
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
using StoryPlatform.Application.Features.MediaGeneration.Models;
using StoryPlatform.Application.Features.MediaGeneration.Services;
using StoryPlatform.Application.Features.MediaStorage.Models;

namespace StoryPlatform.Infrastructure.AI;

/// <summary>
/// Image generation provider — routes to either Vertex AI (OAuth2 Bearer token) or the public
/// Gemini REST API (x-goog-api-key header) based on <c>VertexOptions.UseVertex</c>.
/// Uses AspectRatio applied via generationConfig.responseFormat.image.aspectRatio,
/// validates magic bytes of the returned image, and retries transient transport errors.
/// </summary>
public sealed class GeminiImageGenerationProvider : IImageGenerationProvider
{
    private const string OperationName = "ImageGeneration";
    private readonly HttpClient _httpClient;
    private readonly GeminiOptions _gemini;
    private readonly VertexOptions _vertex;
    private readonly ImageGenerationOptions _image;
    private readonly ILogger<GeminiImageGenerationProvider> _logger;
    private readonly IMediaOutboundPrivacyGuard _privacyGuard;

    public GeminiImageGenerationProvider(
        HttpClient httpClient,
        IOptions<GeminiOptions> geminiOptions,
        IOptions<VertexOptions> vertexOptions,
        IOptions<ImageGenerationOptions> imageOptions,
        ILogger<GeminiImageGenerationProvider> logger,
        IMediaOutboundPrivacyGuard? privacyGuard = null)
    {
        _httpClient = httpClient;
        _gemini = geminiOptions.Value;
        _vertex = vertexOptions.Value;
        _image = imageOptions.Value;
        _logger = logger;
        _privacyGuard = privacyGuard ?? new DeterministicMediaOutboundPrivacyGuard();
        if (_httpClient.Timeout == Timeout.InfiniteTimeSpan || _httpClient.Timeout > TimeSpan.FromSeconds(_image.TimeoutSeconds))
            _httpClient.Timeout = TimeSpan.FromSeconds(_image.TimeoutSeconds);
    }

    public async Task<GeneratedMedia> GenerateAsync(
        SceneSpecification specification, CancellationToken cancellationToken = default)
    {
        var provider = _vertex.UseVertex ? "VertexAI" : "Gemini";
        var elapsed = Stopwatch.StartNew();
        ProviderMediaContextSerializer.Validate(specification.MediaContext);

        var prompt = BuildPrompt(specification, _image);
        try
        {
            _privacyGuard.EnsureSafeForProvider(OperationName, prompt);
        }
        catch (MediaProviderPrivacyException ex)
        {
            MediaProviderDiagnostics.LogFailed(
                _logger, OperationName, provider, _image.Model, elapsed.Elapsed.TotalMilliseconds,
                null, ex.ErrorCode, null, null, ex.GetType().Name);
            throw;
        }

        if (!_vertex.UseVertex && string.IsNullOrWhiteSpace(_gemini.ApiKey))
            throw new PermanentMediaGenerationException("IMAGE_PROVIDER_NOT_CONFIGURED");

        var body = BuildRequestBody(prompt, _image);
        var payloadJson = JsonSerializer.Serialize(body);
        var requestBytes = Encoding.UTF8.GetByteCount(payloadJson);
        MediaProviderDiagnostics.LogStarted(_logger, OperationName, provider, _image.Model, prompt.Length, requestBytes);

        int? httpStatus = null;
        string? providerRequestId = null;
        double? retryAfterSeconds = null;

        try
        {
            using var request = BuildRequest(payloadJson);
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            httpStatus = (int)response.StatusCode;
            providerRequestId = MediaProviderDiagnostics.ExtractAllowlistedRequestId(response);
            retryAfterSeconds = MediaProviderDiagnostics.GetRetryAfterSeconds(response);

            if (!response.IsSuccessStatusCode)
            {
                // Drain body without logging or embedding it in exceptions.
                _ = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                var errorCode = $"GEMINI_IMAGE_HTTP_{(int)response.StatusCode}";
                MediaProviderDiagnostics.LogFailed(
                    _logger, OperationName, provider, _image.Model, elapsed.Elapsed.TotalMilliseconds,
                    httpStatus, errorCode, retryAfterSeconds, providerRequestId,
                    IsTransient(response.StatusCode) ? nameof(TransientMediaGenerationException) : nameof(PermanentMediaGenerationException));

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
            var (bytes, _, usage) = await ExtractFirstInlineImageAsync(stream, cancellationToken).ConfigureAwait(false);

            var validation = MagicByteValidators.ValidateImage(bytes);
            if (!validation.IsValid)
                throw new PermanentMediaGenerationException($"GEMINI_IMAGE_VALIDATION_FAILED: {validation.Reason}");

            MediaProviderDiagnostics.LogCompleted(
                _logger, OperationName, provider, _image.Model, elapsed.Elapsed.TotalMilliseconds,
                httpStatus.Value, providerRequestId, bytes.Length, validation.DetectedMime, usage);

            return new GeneratedMedia(bytes, validation.DetectedMime!, new Dictionary<string, string>
            {
                ["provider"] = provider,
                ["model"] = _image.Model,
                ["aspectRatio"] = _image.AspectRatio
            });
        }
        catch (OperationCanceledException)
        {
            MediaProviderDiagnostics.LogCancelled(
                _logger, OperationName, provider, _image.Model, elapsed.Elapsed.TotalMilliseconds,
                cancellationToken.IsCancellationRequested);
            throw;
        }
        catch (TransientMediaGenerationException)
        {
            throw;
        }
        catch (PermanentMediaGenerationException ex)
        {
            MediaProviderDiagnostics.LogFailed(
                _logger, OperationName, provider, _image.Model, elapsed.Elapsed.TotalMilliseconds,
                httpStatus, ex.ErrorCode, retryAfterSeconds, providerRequestId, ex.GetType().Name);
            throw;
        }
        catch (Exception ex)
        {
            MediaProviderDiagnostics.LogFailed(
                _logger, OperationName, provider, _image.Model, elapsed.Elapsed.TotalMilliseconds,
                httpStatus, "GEMINI_IMAGE_PROVIDER_FAILED", retryAfterSeconds, providerRequestId, ex.GetType().Name);
            throw;
        }
    }

    private HttpRequestMessage BuildRequest(string payloadJson)
    {
        var url = VertexUrlResolver.Resolve(_vertex, _gemini, _image.Model);
        var message = new HttpRequestMessage(HttpMethod.Post, url);
        if (!_vertex.UseVertex)
        {
            message.Headers.Add("x-goog-api-key", _gemini.ApiKey);
        }
        // Bearer token is injected by VertexAuthDelegatingHandler in the HttpClient pipeline.
        message.Content = new StringContent(payloadJson, Encoding.UTF8, "application/json");
        return message;
    }

    private static string BuildPrompt(SceneSpecification spec, ImageGenerationOptions options)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Child-friendly storybook illustration. Avoid any unsafe content for children aged 6-12.");
        sb.AppendLine(IllustrationTextPolicy.Instruction);
        sb.AppendLine($"People policy: {options.PersonGeneration}. Do not depict children in unsafe, sexualized, or exploitative situations.");
        sb.AppendLine("--- Story media context: preserve character appearance, style and safety across all beats ---");
        sb.AppendLine(ProviderMediaContextSerializer.Serialize(spec.MediaContext));
        sb.AppendLine("--- Visual description ---");
        if (!string.IsNullOrWhiteSpace(spec.BeatText))
        {
            sb.AppendLine("Illustrate ONLY this visual beat, while preserving the scene's continuity:");
            sb.AppendLine(spec.BeatText);
            sb.AppendLine("Specific visual focus:");
            sb.AppendLine(spec.BeatVisualFocus);
            sb.AppendLine("Full scene context (do not depict all moments at once):");
            sb.AppendLine(spec.SceneText);
        }
        else sb.AppendLine(spec.VisualDescription ?? spec.SceneText);
        sb.AppendLine("--- Must show ---");
        foreach (var must in spec.MustShow) sb.AppendLine($"- {must}");
        sb.AppendLine("--- Must not contradict ---");
        foreach (var mustNot in spec.MustNotContradict) sb.AppendLine($"- {mustNot}");
        if (!string.IsNullOrWhiteSpace(spec.PromptFeedback))
        {
            sb.AppendLine("--- Constraints from previous failed attempt ---");
            sb.AppendLine(spec.PromptFeedback);
        }
        sb.AppendLine("--- Mandatory final image constraint ---");
        sb.AppendLine(IllustrationTextPolicy.Instruction);
        return sb.ToString();
    }

    private static object BuildRequestBody(string prompt, ImageGenerationOptions options) => new
    {
        contents = new[]
        {
            new { role = "user", parts = new[] { new { text = prompt } } }
        },
        generationConfig = new
        {
            responseModalities = new[] { "TEXT", "IMAGE" },
            candidateCount = 1,
            imageConfig = new
            {
                aspectRatio = options.AspectRatio
            }
        },
        safetySettings = BuildSafetySettings(options.SafetySetting)
    };

    private static object[] BuildSafetySettings(string setting)
    {
        var threshold = setting.Trim().ToLowerInvariant() switch
        {
            "block_most" => "BLOCK_LOW_AND_ABOVE",
            "block_few" => "BLOCK_ONLY_HIGH",
            "block_none" => "BLOCK_NONE",
            _ => "BLOCK_MEDIUM_AND_ABOVE"
        };
        return
        [
            new { category = "HARM_CATEGORY_HARASSMENT", threshold },
            new { category = "HARM_CATEGORY_HATE_SPEECH", threshold },
            new { category = "HARM_CATEGORY_SEXUALLY_EXPLICIT", threshold },
            new { category = "HARM_CATEGORY_DANGEROUS_CONTENT", threshold }
        ];
    }

    private static bool IsTransient(System.Net.HttpStatusCode code) =>
        code is System.Net.HttpStatusCode.TooManyRequests or System.Net.HttpStatusCode.RequestTimeout or
        System.Net.HttpStatusCode.ServiceUnavailable || (int)code >= 500;

    private static async Task<(byte[] bytes, string? mimeHint, ProviderTokenUsage usage)> ExtractFirstInlineImageAsync(
        Stream responseBody, CancellationToken cancellationToken)
    {
        using var ms = new MemoryStream();
        await responseBody.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
        var json = Encoding.UTF8.GetString(ms.ToArray());
        var usage = MediaProviderDiagnostics.ExtractUsageMetadata(json);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("candidates", out var candidates) || candidates.ValueKind != JsonValueKind.Array || candidates.GetArrayLength() == 0)
            throw new PermanentMediaGenerationException("GEMINI_IMAGE_NO_CANDIDATES");
        var parts = candidates[0].GetProperty("content").GetProperty("parts");
        foreach (var part in parts.EnumerateArray())
        {
            if (part.TryGetProperty("inlineData", out var inline))
            {
                var b64 = inline.GetProperty("data").GetString()
                          ?? throw new PermanentMediaGenerationException("GEMINI_IMAGE_EMPTY_DATA");
                var mime = inline.TryGetProperty("mimeType", out var m) ? m.GetString() : null;
                return (Convert.FromBase64String(b64), mime, usage);
            }
        }
        throw new PermanentMediaGenerationException("GEMINI_IMAGE_NO_INLINE_DATA");
    }
}
