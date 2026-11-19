using System;
using System.Diagnostics;
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
/// Gemini alignment evaluator — routes to either Vertex AI (OAuth2 Bearer token) or the public
/// Gemini REST API (x-goog-api-key header) based on <c>VertexOptions.UseVertex</c>.
/// </summary>
public sealed class GeminiMediaAlignmentEvaluator : IMediaAlignmentEvaluator
{
    private const string OperationName = "ImageAlignment";
    private readonly HttpClient _httpClient;
    private readonly GeminiOptions _gemini;
    private readonly VertexOptions _vertex;
    private readonly MediaEvaluationOptions _evalOptions;
    private readonly ILogger<GeminiMediaAlignmentEvaluator> _logger;
    private readonly IMediaOutboundPrivacyGuard _privacyGuard;

    public GeminiMediaAlignmentEvaluator(
        HttpClient httpClient,
        IOptions<GeminiOptions> geminiOptions,
        IOptions<VertexOptions> vertexOptions,
        IOptions<MediaEvaluationOptions> evalOptions,
        ILogger<GeminiMediaAlignmentEvaluator> logger,
        IMediaOutboundPrivacyGuard? privacyGuard = null)
    {
        _httpClient = httpClient;
        _gemini = geminiOptions.Value;
        _vertex = vertexOptions.Value;
        _evalOptions = evalOptions.Value;
        _logger = logger;
        _privacyGuard = privacyGuard ?? new DeterministicMediaOutboundPrivacyGuard();
        if (_httpClient.Timeout == Timeout.InfiniteTimeSpan || _httpClient.Timeout.TotalSeconds > _evalOptions.TimeoutSeconds)
            _httpClient.Timeout = TimeSpan.FromSeconds(_evalOptions.TimeoutSeconds);
    }

    public async Task<MediaEvaluationResult> EvaluateAsync(
        SceneSpecification specification, GeneratedMedia illustration,
        CancellationToken cancellationToken = default)
    {
        var provider = _vertex.UseVertex ? "VertexAI" : "Gemini";
        var elapsed = Stopwatch.StartNew();

        var prompt = BuildPrompt(specification);
        try
        {
            MediaSpecificationPrivacyHelper.EnsureSafe(_privacyGuard, OperationName, specification, prompt);
        }
        catch (MediaProviderPrivacyException ex)
        {
            MediaProviderDiagnostics.LogFailed(
                _logger, OperationName, provider, _evalOptions.Model, elapsed.Elapsed.TotalMilliseconds,
                null, ex.ErrorCode, null, null, ex.GetType().Name);
            throw;
        }

        if (!_vertex.UseVertex && string.IsNullOrWhiteSpace(_gemini.ApiKey))
            return new MediaEvaluationResult(MediaEvaluationDecision.Fail, "EVALUATOR_NOT_CONFIGURED");

        var body = BuildRequestBody(prompt, illustration);
        var payloadJson = JsonSerializer.Serialize(body);
        var requestBytes = Encoding.UTF8.GetByteCount(payloadJson);
        MediaProviderDiagnostics.LogStarted(_logger, OperationName, provider, _evalOptions.Model, prompt.Length, requestBytes);

        int? httpStatus = null;
        string? providerRequestId = null;
        double? retryAfterSeconds = null;

        try
        {
            using var request = BuildRequest(payloadJson);
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

            var statusCode = response.StatusCode;
            httpStatus = (int)statusCode;
            providerRequestId = MediaProviderDiagnostics.ExtractAllowlistedRequestId(response);
            retryAfterSeconds = MediaProviderDiagnostics.GetRetryAfterSeconds(response);
            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var responseBytes = Encoding.UTF8.GetByteCount(content);

            if (!IsSuccess(statusCode))
            {
                var errorCode = $"ALIGNMENT_EVALUATOR_HTTP_{httpStatus}";
                MediaProviderDiagnostics.LogFailed(
                    _logger, OperationName, provider, _evalOptions.Model, elapsed.Elapsed.TotalMilliseconds,
                    httpStatus, errorCode, retryAfterSeconds, providerRequestId,
                    IsTransient(statusCode) ? nameof(TransientMediaGenerationException) : nameof(HttpRequestException));
                if (IsTransient(statusCode))
                {
                    throw new TransientMediaGenerationException(
                        errorCode,
                        new HttpRequestException(errorCode, null, statusCode),
                        MediaProviderDiagnostics.GetRetryAfterDelta(response));
                }
                return new MediaEvaluationResult(MediaEvaluationDecision.Fail, "EVALUATOR_HTTP_ERROR");
            }

            var usage = MediaProviderDiagnostics.ExtractUsageMetadata(content);
            var result = ParseResponse(content);
            MediaProviderDiagnostics.LogCompleted(
                _logger, OperationName, provider, _evalOptions.Model, elapsed.Elapsed.TotalMilliseconds,
                httpStatus.Value, providerRequestId, responseBytes, "application/json", usage);
            return result;
        }
        catch (OperationCanceledException)
        {
            MediaProviderDiagnostics.LogCancelled(
                _logger, OperationName, provider, _evalOptions.Model, elapsed.Elapsed.TotalMilliseconds,
                cancellationToken.IsCancellationRequested);
            throw;
        }
        catch (TransientMediaGenerationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            MediaProviderDiagnostics.LogFailed(
                _logger, OperationName, provider, _evalOptions.Model, elapsed.Elapsed.TotalMilliseconds,
                httpStatus, "ALIGNMENT_EVALUATOR_FAILED", retryAfterSeconds, providerRequestId, ex.GetType().Name);
            throw;
        }
    }

    private HttpRequestMessage BuildRequest(string payloadJson)
    {
        var url = VertexUrlResolver.Resolve(_vertex, _gemini, _evalOptions.Model);
        var message = new HttpRequestMessage(HttpMethod.Post, url);
        if (!_vertex.UseVertex)
        {
            message.Headers.Add("x-goog-api-key", _gemini.ApiKey);
        }
        message.Content = new StringContent(payloadJson, Encoding.UTF8, "application/json");
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
        sb.AppendLine("Evaluate whether the illustration depicts the specified visual beat and does not contradict the full scene.");
        sb.AppendLine(IllustrationTextPolicy.Instruction);
        sb.AppendLine("Inspect the actual image for visible writing, including tiny text or garbled letter-like story captions. Return containsVisibleText=true when any is present, even if the scene otherwise aligns well. Return false only when the image is text-free. Do not infer text presence from the reference story alone.");
        if (!string.IsNullOrWhiteSpace(spec.BeatText))
        {
            sb.AppendLine("BEAT TEXT:");
            sb.AppendLine(spec.BeatText);
            sb.AppendLine("BEAT VISUAL FOCUS:");
            sb.AppendLine(spec.BeatVisualFocus);
        }
        sb.AppendLine();
        sb.AppendLine("SCENE TEXT:");
        sb.AppendLine(spec.SceneText);
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(spec.VisualDescription))
        {
            sb.AppendLine("SCENE VISUAL CONTEXT (the image need not depict every moment):");
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
        sb.AppendLine(@"{ ""alignmentScore"": 0.9, ""containsVisibleText"": false, ""reason"": ""brief explanation"" }");
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
            if (!root.TryGetProperty("containsVisibleText", out var visibleText) ||
                visibleText.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                return new MediaEvaluationResult(MediaEvaluationDecision.Fail, "ILLUSTRATION_TEXT_CHECK_MISSING");
            if (visibleText.GetBoolean())
                return new MediaEvaluationResult(MediaEvaluationDecision.Fail, "ILLUSTRATION_CONTAINS_TEXT");

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
        catch (Exception)
        {
            return new MediaEvaluationResult(MediaEvaluationDecision.Fail, "JSON_PARSE_ERROR");
        }
    }
}
