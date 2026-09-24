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
/// Gemini safety evaluator — routes to either Vertex AI (OAuth2 Bearer token) or the public
/// Gemini REST API (x-goog-api-key header) based on <c>VertexOptions.UseVertex</c>.
/// </summary>
public sealed class GeminiMediaSafetyEvaluator : IMediaSafetyEvaluator
{
    private readonly HttpClient _httpClient;
    private readonly GeminiOptions _gemini;
    private readonly VertexOptions _vertex;
    private readonly MediaEvaluationOptions _evalOptions;
    private readonly ILogger<GeminiMediaSafetyEvaluator> _logger;

    public GeminiMediaSafetyEvaluator(
        HttpClient httpClient,
        IOptions<GeminiOptions> geminiOptions,
        IOptions<VertexOptions> vertexOptions,
        IOptions<MediaEvaluationOptions> evalOptions,
        ILogger<GeminiMediaSafetyEvaluator> logger)
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
            _logger.LogError("Safety evaluator HTTP {Status}: {Body}", (int)statusCode, Truncate(content, 200));
            if (IsTransient(statusCode))
                throw new HttpRequestException($"SAFETY_EVALUATOR_TRANSIENT_HTTP_{(int)statusCode}");
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
        sb.AppendLine("You are a child safety content reviewer for illustrations in stories for children aged 6-12.");
        sb.AppendLine("Check whether the described content is appropriate.");
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
        sb.AppendLine("Concerns to flag: violence, frightening content, sexual content, discrimination, dangerous activities, personally identifiable information, graphic medical imagery.");
        sb.AppendLine();
        sb.AppendLine("Respond ONLY with valid JSON:");
        sb.AppendLine(@"{ ""isSafe"": true/false, ""concerns"": [""concern1"", ""concern2""] }");
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
            var isSafe = root.TryGetProperty("isSafe", out var s) && s.ValueKind == JsonValueKind.True;
            if (isSafe) return new MediaEvaluationResult(MediaEvaluationDecision.Pass);
            var concerns = root.TryGetProperty("concerns", out var c) && c.ValueKind == JsonValueKind.Array
                ? string.Join("; ", c.EnumerateArray().Select(x => x.GetString()))
                : "Safety concerns detected";
            return new MediaEvaluationResult(MediaEvaluationDecision.Fail, concerns);
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
