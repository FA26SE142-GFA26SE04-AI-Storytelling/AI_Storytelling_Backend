using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using StoryPlatform.AI.Application.Abstractions.LLM;
using StoryPlatform.AI.Domain.Generation;

namespace StoryPlatform.AI.Infrastructure.LLM.Gemini;

public sealed class GeminiLlmClient : ILlmClient
{
    private readonly HttpClient _httpClient;
    private readonly GeminiOptions _options;

    public GeminiLlmClient(HttpClient httpClient, IOptions<GeminiOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _httpClient.Timeout = TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 10, 300));
    }

    public async Task<LlmGenerationResult> GenerateStructuredAsync(
        string prompt,
        string schemaName,
        JsonElement schema,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            throw new InvalidOperationException("AI:Gemini:ApiKey (or GEMINI_API_KEY) is not configured.");
        if (string.IsNullOrWhiteSpace(_options.Model))
            throw new InvalidOperationException("AI:Gemini:Model is not configured.");

        var endpoint = BuildEndpoint();
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.TryAddWithoutValidation("x-goog-api-key", _options.ApiKey);
        request.Content = JsonContent.Create(new
        {
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new[] { new { text = prompt } }
                }
            },
            generationConfig = new
            {
                responseMimeType = "application/json",
                responseSchema = schema
            }
        });

        var stopwatch = Stopwatch.StartNew();
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        stopwatch.Stop();

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"LLM provider returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).",
                null,
                response.StatusCode);
        }

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var content = ExtractOutputText(root)
                      ?? throw new InvalidOperationException("LLM response did not contain structured output text.");
        var modelVersion = root.TryGetProperty("modelVersion", out var modelElement)
            ? modelElement.GetString() ?? _options.Model
            : _options.Model;
        var usage = root.TryGetProperty("usageMetadata", out var usageElement) ? usageElement : default;

        return new LlmGenerationResult(
            content,
            "Gemini",
            _options.Model,
            modelVersion,
            GetInt32(usage, "promptTokenCount"),
            GetInt32(usage, "candidatesTokenCount"),
            stopwatch.ElapsedMilliseconds);
    }

    private Uri BuildEndpoint()
    {
        if (!Uri.TryCreate(_options.Endpoint.TrimEnd('/') + '/', UriKind.Absolute, out var baseUri) ||
            baseUri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("AI:Gemini:Endpoint must be a valid HTTPS URL.");

        var model = Uri.EscapeDataString(_options.Model);
        return new Uri(baseUri, $"models/{model}:generateContent");
    }

    private static string? ExtractOutputText(JsonElement root)
    {
        if (!root.TryGetProperty("candidates", out var candidates) || candidates.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var candidate in candidates.EnumerateArray())
        {
            if (!candidate.TryGetProperty("content", out var content) ||
                !content.TryGetProperty("parts", out var parts) ||
                parts.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var part in parts.EnumerateArray())
                if (part.TryGetProperty("text", out var text) && !string.IsNullOrWhiteSpace(text.GetString()))
                    return text.GetString();
        }

        return null;
    }

    private static int GetInt32(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(propertyName, out var value) &&
        value.TryGetInt32(out var result)
            ? result
            : 0;
}
