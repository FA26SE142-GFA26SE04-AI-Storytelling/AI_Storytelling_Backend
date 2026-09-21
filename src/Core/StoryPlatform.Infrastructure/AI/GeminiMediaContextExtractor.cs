using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StoryPlatform.Application.Features.MediaGeneration.Interfaces;
using StoryPlatform.Application.Features.MediaGeneration.Models;
using StoryPlatform.Infrastructure.AI;

namespace StoryPlatform.Infrastructure.AI;

/// <summary>
/// Gemini text-only extraction of rich semantic context from a story version.
/// Uses gemini-2.5-flash to extract characters, visual style guidance, and safety constraints.
/// </summary>
public sealed class GeminiMediaContextExtractor : IMediaContextExtractor
{
    private readonly HttpClient _httpClient;
    private readonly GeminiOptions _gemini;
    private readonly ILogger<GeminiMediaContextExtractor> _logger;

    public GeminiMediaContextExtractor(
        HttpClient httpClient,
        IOptions<GeminiOptions> geminiOptions,
        ILogger<GeminiMediaContextExtractor> logger)
    {
        _httpClient = httpClient;
        _gemini = geminiOptions.Value;
        _logger = logger;
        if (_httpClient.Timeout == Timeout.InfiniteTimeSpan || _httpClient.Timeout.TotalSeconds > _gemini.TimeoutSeconds)
            _httpClient.Timeout = TimeSpan.FromSeconds(_gemini.TimeoutSeconds);
    }

    public async Task<ExtractedMediaContext> ExtractAsync(
        MediaContextBuildRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_gemini.ApiKey))
            return FallbackResult();

        var prompt = BuildPrompt(request);
        var body = BuildRequestBody(prompt);
        HttpResponseMessage? response = null;
        try
        {
            response = await GeminiHttpRetry.SendWithTransportRetryAsync(
                () => BuildRequest(body),
                _httpClient,
                _gemini.TransportRetryCount,
                TimeSpan.FromMilliseconds(_gemini.TransportRetryBaseDelayMs),
                _logger,
                cancellationToken).ConfigureAwait(false);

            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Context extractor HTTP {Status}, using fallback", response.StatusCode);
                return FallbackResult();
            }
            return ParseResponse(content);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Context extraction failed, using fallback");
            return FallbackResult();
        }
        finally
        {
            response?.Dispose();
        }
    }

    private static string BuildPrompt(MediaContextBuildRequest request)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Extract semantic context from this children's story for illustration generation.");
        sb.AppendLine();
        sb.AppendLine("STORY TITLE: " + request.Title);
        sb.AppendLine("LESSON: " + (request.Lesson ?? "(none)"));
        sb.AppendLine("CONTENT:");
        sb.AppendLine(request.Content);
        sb.AppendLine();
        sb.AppendLine("Respond ONLY with valid JSON:");
        sb.AppendLine(@"{ ""characterList"": ""..."", ""visualStyleGuidance"": ""..."", ""safetyConstraints"": ""..."" }");
        return sb.ToString();
    }

    private static object BuildRequestBody(string prompt) => new
    {
        contents = new[] { new { role = "user", parts = new[] { new { text = prompt } } } }
    };

    private HttpRequestMessage BuildRequest(object body)
    {
        var url = $"{_gemini.Endpoint.TrimEnd('/')}/gemini-2.5-flash:generateContent";
        var msg = new HttpRequestMessage(HttpMethod.Post, url);
        msg.Headers.Add("x-goog-api-key", _gemini.ApiKey);
        msg.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        return msg;
    }

    private static ExtractedMediaContext ParseResponse(string content)
    {
        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            var chars = root.TryGetProperty("characterList", out var c) ? c.GetString() : null;
            var style = root.TryGetProperty("visualStyleGuidance", out var s) ? s.GetString() : null;
            var safety = root.TryGetProperty("safetyConstraints", out var sc) ? sc.GetString() : null;
            return new ExtractedMediaContext(content, chars, style, safety);
        }
        catch
        {
            return new ExtractedMediaContext(content, null, null, null);
        }
    }

    private static ExtractedMediaContext FallbackResult() =>
        new("{}", null, null, null);
}
