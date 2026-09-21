using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StoryPlatform.Application.Features.MediaGeneration;
using StoryPlatform.Application.Features.MediaGeneration.Interfaces;
using StoryPlatform.Application.Features.MediaGeneration.Models;
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
    private readonly HttpClient _httpClient;
    private readonly GeminiOptions _gemini;
    private readonly VertexOptions _vertex;
    private readonly ImageGenerationOptions _image;
    private readonly ILogger<GeminiImageGenerationProvider> _logger;

    public GeminiImageGenerationProvider(
        HttpClient httpClient,
        IOptions<GeminiOptions> geminiOptions,
        IOptions<VertexOptions> vertexOptions,
        IOptions<ImageGenerationOptions> imageOptions,
        ILogger<GeminiImageGenerationProvider> logger)
    {
        _httpClient = httpClient;
        _gemini = geminiOptions.Value;
        _vertex = vertexOptions.Value;
        _image = imageOptions.Value;
        _logger = logger;
        if (_httpClient.Timeout == Timeout.InfiniteTimeSpan || _httpClient.Timeout > TimeSpan.FromSeconds(_gemini.TimeoutSeconds))
            _httpClient.Timeout = TimeSpan.FromSeconds(_gemini.TimeoutSeconds);
    }

    public async Task<GeneratedMedia> GenerateAsync(
        SceneSpecification specification, CancellationToken cancellationToken = default)
    {
        if (!_vertex.UseVertex && string.IsNullOrWhiteSpace(_gemini.ApiKey))
            throw new PermanentMediaGenerationException("IMAGE_PROVIDER_NOT_CONFIGURED");

        var prompt = BuildPrompt(specification);
        var body = BuildRequestBody(prompt, _image.AspectRatio);
        using var response = await GeminiHttpRetry.SendWithTransportRetryAsync(
            () => BuildRequest(body),
            _httpClient,
            _image.TransportRetryCount,
            TimeSpan.FromMilliseconds(_image.TransportRetryBaseDelayMs),
            _logger,
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new PermanentMediaGenerationException($"GEMINI_IMAGE_HTTP_{(int)response.StatusCode}: {Truncate(error, 200)}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var (bytes, detectedMime) = await ExtractFirstInlineImageAsync(stream, cancellationToken).ConfigureAwait(false);

        var validation = MagicByteValidators.ValidateImage(bytes);
        if (!validation.IsValid)
            throw new PermanentMediaGenerationException($"GEMINI_IMAGE_VALIDATION_FAILED: {validation.Reason}");

        return new GeneratedMedia(bytes, validation.DetectedMime!, new Dictionary<string, string>
        {
            ["provider"] = _vertex.UseVertex ? "VertexAI" : "Gemini",
            ["model"] = _image.Model,
            ["aspectRatio"] = _image.AspectRatio
        });
    }

    private HttpRequestMessage BuildRequest(object body)
    {
        var url = _vertex.UseVertex
            ? $"https://{_vertex.Location}-aiplatform.googleapis.com/v1/projects/{_vertex.ProjectId}/locations/{_vertex.Location}/publishers/google/models/{_image.Model}:generateContent"
            : $"{_gemini.Endpoint.TrimEnd('/')}/{_image.Model}:generateContent";

        var message = new HttpRequestMessage(HttpMethod.Post, url);
        if (!_vertex.UseVertex)
        {
            message.Headers.Add("x-goog-api-key", _gemini.ApiKey);
        }
        // Bearer token is injected by VertexAuthDelegatingHandler in the HttpClient pipeline.
        message.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        return message;
    }

    private static string BuildPrompt(SceneSpecification spec)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Child-friendly storybook illustration. Avoid any unsafe content for children aged 6-12.");
        sb.AppendLine("--- Visual description ---");
        sb.AppendLine(spec.VisualDescription ?? spec.SceneText);
        sb.AppendLine("--- Must show ---");
        foreach (var must in spec.MustShow) sb.AppendLine($"- {must}");
        sb.AppendLine("--- Must not contradict ---");
        foreach (var mustNot in spec.MustNotContradict) sb.AppendLine($"- {mustNot}");
        if (!string.IsNullOrWhiteSpace(spec.PromptFeedback))
        {
            sb.AppendLine("--- Constraints from previous failed attempt ---");
            sb.AppendLine(spec.PromptFeedback);
        }
        return sb.ToString();
    }

    private static object BuildRequestBody(string prompt, string aspectRatio) => new
    {
        contents = new[]
        {
            new { role = "user", parts = new[] { new { text = prompt } } }
        },
        generationConfig = new
        {
            responseModalities = new[] { "IMAGE" },
            responseFormat = new
            {
                @image = new { aspectRatio }
            }
        }
    };

    private static async Task<(byte[] bytes, string? mimeHint)> ExtractFirstInlineImageAsync(
        Stream responseBody, CancellationToken cancellationToken)
    {
        using var ms = new MemoryStream();
        await responseBody.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
        var json = Encoding.UTF8.GetString(ms.ToArray());
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
                return (Convert.FromBase64String(b64), mime);
            }
        }
        throw new PermanentMediaGenerationException("GEMINI_IMAGE_NO_INLINE_DATA");
    }

    private static string Truncate(string value, int max) =>
        string.IsNullOrEmpty(value) ? string.Empty :
        value.Length <= max ? value : value.Substring(0, max);
}
