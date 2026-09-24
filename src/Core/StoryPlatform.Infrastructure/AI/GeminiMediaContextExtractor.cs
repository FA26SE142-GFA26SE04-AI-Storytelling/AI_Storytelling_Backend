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
/// Routes to either Vertex AI (OAuth2 Bearer token) or the public Gemini REST API
/// (x-goog-api-key header) based on <c>VertexOptions.UseVertex</c>.
/// </summary>
public sealed class GeminiMediaContextExtractor : IMediaContextExtractor
{
    private readonly HttpClient _httpClient;
    private readonly GeminiOptions _gemini;
    private readonly VertexOptions _vertex;
    private readonly ILogger<GeminiMediaContextExtractor> _logger;

    public GeminiMediaContextExtractor(
        HttpClient httpClient,
        IOptions<GeminiOptions> geminiOptions,
        IOptions<VertexOptions> vertexOptions,
        ILogger<GeminiMediaContextExtractor> logger)
    {
        _httpClient = httpClient;
        _gemini = geminiOptions.Value;
        _vertex = vertexOptions.Value;
        _logger = logger;
        if (_httpClient.Timeout == Timeout.InfiniteTimeSpan || _httpClient.Timeout.TotalSeconds > _gemini.TimeoutSeconds)
            _httpClient.Timeout = TimeSpan.FromSeconds(_gemini.TimeoutSeconds);
    }

    public async Task<ExtractedMediaContext> ExtractAsync(
        MediaContextBuildRequest request, CancellationToken cancellationToken = default)
    {
        if (!_vertex.UseVertex && string.IsNullOrWhiteSpace(_gemini.ApiKey))
            return FallbackResult();

        try
        {
            var prompt = BuildPrompt(request);
            var body = BuildRequestBody(prompt);
            using var httpRequest = BuildRequest(body);
            using var response = await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);

            var statusCode = response.StatusCode;
            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!IsSuccess(statusCode))
            {
                _logger.LogWarning("Context extractor HTTP {Status}, using fallback", (int)statusCode);
                return FallbackResult();
            }
            return ParseResponse(content);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Context extraction failed, using fallback");
            return FallbackResult();
        }
    }

    private static bool IsSuccess(System.Net.HttpStatusCode code)
    {
        var n = (int)code;
        return n >= 200 && n < 300;
    }

    private HttpRequestMessage BuildRequest(object body)
    {
        var url = VertexUrlResolver.Resolve(_vertex, _gemini, "gemini-3.8-flash");
        var msg = new HttpRequestMessage(HttpMethod.Post, url);
        if (!_vertex.UseVertex)
        {
            msg.Headers.Add("x-goog-api-key", _gemini.ApiKey);
        }
        msg.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        return msg;
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

    private static ExtractedMediaContext ParseResponse(string content)
    {
        try
        {
            var payload = GeminiResponseJson.ExtractFirstTextPayload(content);
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            var chars = root.TryGetProperty("characterList", out var c) ? c.GetString() : null;
            var style = root.TryGetProperty("visualStyleGuidance", out var s) ? s.GetString() : null;
            var safety = root.TryGetProperty("safetyConstraints", out var sc) ? sc.GetString() : null;
            return new ExtractedMediaContext(payload, chars, style, safety);
        }
        catch
        {
            return new ExtractedMediaContext(content, null, null, null);
        }
    }

    private static ExtractedMediaContext FallbackResult() =>
        new("{}", null, null, null);
}
