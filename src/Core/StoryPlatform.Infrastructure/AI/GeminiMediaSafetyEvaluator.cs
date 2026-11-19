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
/// Gemini safety evaluator — routes to either Vertex AI (OAuth2 Bearer token) or the public
/// Gemini REST API (x-goog-api-key header) based on <c>VertexOptions.UseVertex</c>.
/// </summary>
public sealed class GeminiMediaSafetyEvaluator : IMediaSafetyEvaluator
{
    private const string OperationName = "ImageSafety";
    private readonly HttpClient _httpClient;
    private readonly GeminiOptions _gemini;
    private readonly VertexOptions _vertex;
    private readonly MediaEvaluationOptions _evalOptions;
    private readonly ILogger<GeminiMediaSafetyEvaluator> _logger;
    private readonly IMediaOutboundPrivacyGuard _privacyGuard;

    public GeminiMediaSafetyEvaluator(
        HttpClient httpClient,
        IOptions<GeminiOptions> geminiOptions,
        IOptions<VertexOptions> vertexOptions,
        IOptions<MediaEvaluationOptions> evalOptions,
        ILogger<GeminiMediaSafetyEvaluator> logger,
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
                var errorCode = $"SAFETY_EVALUATOR_HTTP_{httpStatus}";
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
                httpStatus, "SAFETY_EVALUATOR_FAILED", retryAfterSeconds, providerRequestId, ex.GetType().Name);
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
        sb.AppendLine("You are a child safety content reviewer for illustrations in stories for children aged 6-12.");
        sb.AppendLine("Check whether the described content is appropriate.");
        sb.AppendLine();
        sb.AppendLine("SCENE TEXT:");
        sb.AppendLine(spec.SceneText);
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(spec.BeatText))
        {
            sb.AppendLine("ILLUSTRATED BEAT:");
            sb.AppendLine(spec.BeatText);
            sb.AppendLine(spec.BeatVisualFocus);
            sb.AppendLine();
        }
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
        catch (Exception)
        {
            return new MediaEvaluationResult(MediaEvaluationDecision.Fail, "JSON_PARSE_ERROR");
        }
    }
}
