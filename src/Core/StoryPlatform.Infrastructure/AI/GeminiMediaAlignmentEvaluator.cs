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
using StoryPlatform.Application.Features.MediaStorage.Models;

namespace StoryPlatform.Infrastructure.AI;

/// <summary>
/// Gemini alignment evaluator — routes to either Vertex AI (OAuth2 Bearer token) or the public
/// Gemini REST API (x-goog-api-key header) based on <c>VertexOptions.UseVertex</c>.
/// </summary>
public sealed class GeminiMediaAlignmentEvaluator : IMediaAlignmentEvaluator
{
    private readonly HttpClient _httpClient;
    private readonly GeminiOptions _gemini;
    private readonly VertexOptions _vertex;
    private readonly MediaEvaluationOptions _evalOptions;
    private readonly ILogger<GeminiMediaAlignmentEvaluator> _logger;

    public GeminiMediaAlignmentEvaluator(
        HttpClient httpClient,
        IOptions<GeminiOptions> geminiOptions,
        IOptions<VertexOptions> vertexOptions,
        IOptions<MediaEvaluationOptions> evalOptions,
        ILogger<GeminiMediaAlignmentEvaluator> logger)
    {
        _httpClient = httpClient;
        _gemini = geminiOptions.Value;
        _vertex = vertexOptions.Value;
        _evalOptions = evalOptions.Value;
        _logger = logger;
        if (_httpClient.Timeout == Timeout.InfiniteTimeSpan || _httpClient.Timeout.TotalSeconds > _gemini.TimeoutSeconds)
            _httpClient.Timeout = TimeSpan.FromSeconds(_gemini.TimeoutSeconds);
    }

    public async Task<MediaEvaluationResult> EvaluateAsync(
        SceneSpecification specification, GeneratedMedia illustration,
        CancellationToken cancellationToken = default)
    {
        if (!_vertex.UseVertex && string.IsNullOrWhiteSpace(_gemini.ApiKey))
            return new MediaEvaluationResult(MediaEvaluationDecision.Fail, "EVALUATOR_NOT_CONFIGURED");

        var prompt = BuildPrompt(specification);
        var body = BuildRequestBody(prompt);
        using var response = await GeminiHttpRetry.SendWithTransportRetryAsync(
            () => BuildRequest(body),
            _httpClient,
            _evalOptions.TransportRetryCount,
            TimeSpan.FromMilliseconds(_gemini.TransportRetryBaseDelayMs),
            _logger,
            cancellationToken).ConfigureAwait(false);

        var statusCode = response.StatusCode;
        var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!IsSuccess(statusCode))
        {
            _logger.LogError("Alignment evaluator HTTP {Status}: {Body}", (int)statusCode, Truncate(content, 200));
            return new MediaEvaluationResult(MediaEvaluationDecision.Fail, "EVALUATOR_HTTP_ERROR");
        }

        return ParseResponse(content);
    }

    private HttpRequestMessage BuildRequest(object body)
    {
        var url = _vertex.UseVertex
            ? $"https://{_vertex.Location}-aiplatform.googleapis.com/v1/projects/{_vertex.ProjectId}/locations/{_vertex.Location}/publishers/google/models/{_evalOptions.Model}:generateContent"
            : $"{_gemini.Endpoint.TrimEnd('/')}/{_evalOptions.Model}:generateContent";

        var message = new HttpRequestMessage(HttpMethod.Post, url);
        if (!_vertex.UseVertex)
        {
            message.Headers.Add("x-goog-api-key", _gemini.ApiKey);
        }
        message.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        return message;
    }

    private static bool IsSuccess(System.Net.HttpStatusCode code)
    {
        var n = (int)code;
        return n >= 200 && n < 300;
    }

    private static string BuildPrompt(SceneSpecification spec)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are a story illustration quality evaluator for children aged 6-12.");
        sb.AppendLine("Evaluate whether the described illustration aligns with the story scene below.");
        sb.AppendLine();
        sb.AppendLine("SCENE TEXT:");
        sb.AppendLine(spec.SceneText);
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(spec.VisualDescription))
        {
            sb.AppendLine("VISUAL DESCRIPTION:");
            sb.AppendLine(spec.VisualDescription);
            sb.AppendLine();
        }
        sb.AppendLine("MUST SHOW:");
        foreach (var m in spec.MustShow) sb.AppendLine($"- {m}");
        sb.AppendLine();
        sb.AppendLine("MUST NOT CONTRADICT:");
        foreach (var m in spec.MustNotContradict) sb.AppendLine($"- {m}");
        sb.AppendLine();
        sb.AppendLine("Respond ONLY with valid JSON:");
        sb.AppendLine(@"{ ""alignmentScore"": 0.0-1.0, ""reason"": ""brief explanation"" }");
        return sb.ToString();
    }

    private static object BuildRequestBody(string prompt) => new
    {
        contents = new[] { new { role = "user", parts = new[] { new { text = prompt } } } }
    };

    private static MediaEvaluationResult ParseResponse(string content)
    {
        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            var score = root.TryGetProperty("alignmentScore", out var s) && s.ValueKind == JsonValueKind.Number
                ? s.GetDouble() : -1.0;
            var reason = root.TryGetProperty("reason", out var r) ? r.GetString() : null;
            return score >= 0.75
                ? new MediaEvaluationResult(MediaEvaluationDecision.Pass, reason)
                : new MediaEvaluationResult(MediaEvaluationDecision.Fail, reason ?? "Low alignment score");
        }
        catch (Exception ex)
        {
            return new MediaEvaluationResult(MediaEvaluationDecision.Fail, $"JSON_PARSE_ERROR: {ex.Message}");
        }
    }

    private static string Truncate(string value, int max) =>
        string.IsNullOrEmpty(value) ? string.Empty :
        value.Length <= max ? value : value[..max];
}
