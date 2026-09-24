using System;
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
        if (_httpClient.Timeout == Timeout.InfiniteTimeSpan || _httpClient.Timeout.TotalSeconds > _evalOptions.TimeoutSeconds)
            _httpClient.Timeout = TimeSpan.FromSeconds(_evalOptions.TimeoutSeconds);
    }

    public async Task<MediaEvaluationResult> EvaluateAsync(
        SceneSpecification specification, GeneratedMedia illustration,
        CancellationToken cancellationToken = default)
    {
        if (!_vertex.UseVertex && string.IsNullOrWhiteSpace(_gemini.ApiKey))
            return new MediaEvaluationResult(MediaEvaluationDecision.Fail, "EVALUATOR_NOT_CONFIGURED");

        var prompt = BuildPrompt(specification);
        var body = BuildRequestBody(prompt, illustration);
        using var request = BuildRequest(body);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        var statusCode = response.StatusCode;
        var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!IsSuccess(statusCode))
        {
            _logger.LogError("Alignment evaluator HTTP {Status}: {Body}", (int)statusCode, Truncate(content, 200));
            if (IsTransient(statusCode))
            {
                var errorCode = $"ALIGNMENT_EVALUATOR_HTTP_{(int)statusCode}";
                throw new TransientMediaGenerationException(
                    errorCode,
                    new HttpRequestException(errorCode, null, statusCode));
            }
            return new MediaEvaluationResult(MediaEvaluationDecision.Fail, "EVALUATOR_HTTP_ERROR");
        }

        return ParseResponse(content);
    }

    private HttpRequestMessage BuildRequest(object body)
    {
        var url = VertexUrlResolver.Resolve(_vertex, _gemini, _evalOptions.Model);
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

    private static bool IsTransient(System.Net.HttpStatusCode code) =>
        code is System.Net.HttpStatusCode.TooManyRequests or System.Net.HttpStatusCode.RequestTimeout or
        System.Net.HttpStatusCode.ServiceUnavailable || (int)code >= 500;

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
        sb.AppendLine(@"{ ""alignmentScore"": 0.9, ""reason"": ""brief explanation"" }");
        sb.AppendLine("Scoring guide: 0.7-1.0 = aligns well with key scene elements; below 0.7 = contradicts key elements.");
        return sb.ToString();
    }

    private static object BuildRequestBody(string prompt, GeneratedMedia illustration) => new
    {
        contents = new[]
        {
            new
            {
                role = "user",
                parts = new object[]
                {
                    new { text = prompt },
                    new
                    {
                        inlineData = new
                        {
                            mimeType = illustration.MimeType,
                            data = Convert.ToBase64String(illustration.Content)
                        }
                    }
                }
            }
        }
    };

    private static MediaEvaluationResult ParseResponse(string content)
    {
        try
        {
            using var doc = JsonDocument.Parse(GeminiResponseJson.ExtractFirstTextPayload(content));
            var root = doc.RootElement;

            JsonElement scoreElem = default;
            bool hasScore = root.TryGetProperty("alignmentScore", out scoreElem) ||
                            root.TryGetProperty("alignment_score", out scoreElem) ||
                            root.TryGetProperty("score", out scoreElem);

            double score = -1.0;
            if (hasScore)
            {
                if (scoreElem.ValueKind == JsonValueKind.Number && scoreElem.TryGetDouble(out var num))
                {
                    score = num;
                }
                else if (scoreElem.ValueKind == JsonValueKind.String)
                {
                    var str = scoreElem.GetString()?.Trim();
                    if (double.TryParse(str, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                    {
                        score = parsed;
                    }
                    else if (string.Equals(str, "pass", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(str, "good", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(str, "high", StringComparison.OrdinalIgnoreCase))
                    {
                        score = 1.0;
                    }
                }
            }

            string? reason = null;
            if (root.TryGetProperty("reason", out var r) ||
                root.TryGetProperty("alignmentReason", out r) ||
                root.TryGetProperty("explanation", out r))
            {
                reason = r.GetString();
            }

            bool isPass = score >= 0.70;
            if (!isPass && score < 0 && !string.IsNullOrWhiteSpace(reason))
            {
                if (reason.Contains("aligning well", StringComparison.OrdinalIgnoreCase) ||
                    reason.Contains("matches", StringComparison.OrdinalIgnoreCase) ||
                    reason.Contains("consistent", StringComparison.OrdinalIgnoreCase))
                {
                    isPass = true;
                }
            }

            return isPass
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
