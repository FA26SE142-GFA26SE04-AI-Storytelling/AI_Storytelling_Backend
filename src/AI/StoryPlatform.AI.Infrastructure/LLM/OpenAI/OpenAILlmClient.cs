using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using StoryPlatform.AI.Application.Abstractions.LLM;
using StoryPlatform.AI.Domain.Generation;

namespace StoryPlatform.AI.Infrastructure.LLM.OpenAI;

public sealed class OpenAILlmClient : ILlmClient
{
    private readonly HttpClient _httpClient;
    private readonly OpenAIOptions _options;

    public OpenAILlmClient(HttpClient httpClient, IOptions<OpenAIOptions> options)
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
        {
            throw new InvalidOperationException("AI:OpenAI:ApiKey (or AI__OpenAI__ApiKey) is not configured.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        request.Content = JsonContent.Create(new
        {
            model = _options.Model,
            input = prompt,
            text = new
            {
                format = new
                {
                    type = "json_schema",
                    name = schemaName,
                    strict = true,
                    schema
                }
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
        var model = root.TryGetProperty("model", out var modelElement) ? modelElement.GetString() ?? _options.Model : _options.Model;
        var usage = root.TryGetProperty("usage", out var usageElement) ? usageElement : default;

        return new LlmGenerationResult(
            content,
            "OpenAI",
            model,
            model,
            GetInt32(usage, "input_tokens"),
            GetInt32(usage, "output_tokens"),
            stopwatch.ElapsedMilliseconds);
    }

    private static string? ExtractOutputText(JsonElement root)
    {
        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var part in content.EnumerateArray())
            {
                if (part.TryGetProperty("type", out var type) && type.GetString() == "output_text" &&
                    part.TryGetProperty("text", out var text))
                {
                    return text.GetString();
                }
            }
        }

        return null;
    }

    private static int GetInt32(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out var value)
            ? value.GetInt32()
            : 0;
}
