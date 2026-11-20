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

namespace StoryPlatform.Infrastructure.AI;

/// <summary>
/// Gemini semantic scene segmentation provider — routes to either Vertex AI (OAuth2 Bearer token)
/// or the public Gemini REST API (x-goog-api-key header) based on <c>VertexOptions.UseVertex</c>.
/// </summary>
public sealed class GeminiSemanticSceneSegmentationProvider : ISemanticSceneSegmentationProvider
{
    private readonly HttpClient _httpClient;
    private readonly GeminiOptions _gemini;
    private readonly VertexOptions _vertex;
    private readonly ILogger<GeminiSemanticSceneSegmentationProvider> _logger;
    private readonly IMediaOutboundPrivacyGuard _privacyGuard;
    private const string SemanticModel = "gemini-3.8-flash";
    private const string OperationName = "SemanticSceneSegmentation";

    public GeminiSemanticSceneSegmentationProvider(
        HttpClient httpClient,
        IOptions<GeminiOptions> geminiOptions,
        IOptions<VertexOptions> vertexOptions,
        ILogger<GeminiSemanticSceneSegmentationProvider> logger,
        IMediaOutboundPrivacyGuard? privacyGuard = null)
    {
        _httpClient = httpClient;
        _gemini = geminiOptions.Value;
        _vertex = vertexOptions.Value;
        _logger = logger;
        _privacyGuard = privacyGuard ?? new DeterministicMediaOutboundPrivacyGuard();
        if (_httpClient.Timeout == Timeout.InfiniteTimeSpan || _httpClient.Timeout.TotalSeconds > _gemini.TimeoutSeconds)
            _httpClient.Timeout = TimeSpan.FromSeconds(_gemini.TimeoutSeconds);
    }

    public async Task<IReadOnlyList<SceneSelection>> SegmentAsync(
        SceneSegmentationRequest request, CancellationToken cancellationToken = default)
    {
        var provider = _vertex.UseVertex ? "VertexAI" : "Gemini";
        var elapsed = Stopwatch.StartNew();
        ProviderMediaContextSerializer.Validate(request.MediaContext);

        var prompt = BuildPrompt(request);
        try
        {
            _privacyGuard.EnsureSafeForProvider(OperationName, prompt);
        }
        catch (MediaProviderPrivacyException ex)
        {
            MediaProviderDiagnostics.LogFailed(
                _logger, OperationName, provider, SemanticModel, elapsed.Elapsed.TotalMilliseconds,
                null, ex.ErrorCode, null, null, ex.GetType().Name);
            throw;
        }

        if (!_vertex.UseVertex && string.IsNullOrWhiteSpace(_gemini.ApiKey))
        {
            _logger.LogWarning("Gemini API key not configured and Vertex is disabled, returning empty — orchestrator will fallback");
            return Array.Empty<SceneSelection>();
        }

        var body = new { contents = new[] { new { role = "user", parts = new[] { new { text = prompt } } } } };
        var payloadJson = JsonSerializer.Serialize(body);
        var requestBytes = Encoding.UTF8.GetByteCount(payloadJson);
        MediaProviderDiagnostics.LogStarted(_logger, OperationName, provider, SemanticModel, prompt.Length, requestBytes);

        try
        {
            using var httpRequest = BuildRequest(payloadJson);
            using var response = await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);

            var statusCode = response.StatusCode;
            var providerRequestId = MediaProviderDiagnostics.ExtractAllowlistedRequestId(response);
            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var responseBytes = Encoding.UTF8.GetByteCount(content);

            if (!IsSuccess(statusCode))
            {
                var errorCode = $"GEMINI_SEGMENTATION_HTTP_{(int)statusCode}";
                MediaProviderDiagnostics.LogFailed(
                    _logger, OperationName, provider, SemanticModel, elapsed.Elapsed.TotalMilliseconds,
                    (int)statusCode, errorCode, MediaProviderDiagnostics.GetRetryAfterSeconds(response),
                    providerRequestId, nameof(HttpRequestException));
                return Array.Empty<SceneSelection>();
            }

            var usage = MediaProviderDiagnostics.ExtractUsageMetadata(content);
            var parsed = ParseResponse(content);
            MediaProviderDiagnostics.LogCompleted(
                _logger, OperationName, provider, SemanticModel, elapsed.Elapsed.TotalMilliseconds,
                (int)statusCode, providerRequestId, responseBytes, "application/json", usage);
            return parsed;
        }
        catch (OperationCanceledException)
        {
            MediaProviderDiagnostics.LogCancelled(
                _logger, OperationName, provider, SemanticModel, elapsed.Elapsed.TotalMilliseconds,
                cancellationToken.IsCancellationRequested);
            throw;
        }
        catch (Exception ex)
        {
            MediaProviderDiagnostics.LogFailed(
                _logger, OperationName, provider, SemanticModel, elapsed.Elapsed.TotalMilliseconds,
                null, "GEMINI_SEGMENTATION_FAILED", null, null, ex.GetType().Name);
            throw;
        }
    }

    private static bool IsSuccess(System.Net.HttpStatusCode code)
    {
        var n = (int)code;
        return n >= 200 && n < 300;
    }

    private static string BuildPrompt(SceneSegmentationRequest request)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are a story scene segmentation assistant for children's stories.");
        sb.AppendLine("Given the following story paragraphs and context, decide how to group them into coherent scenes for illustration.");
        sb.AppendLine();
        sb.AppendLine("CONTEXT:");
        sb.AppendLine(ProviderMediaContextSerializer.Serialize(request.MediaContext));
        sb.AppendLine();
        sb.AppendLine("PARAGRAPHS:");
        for (var i = 0; i < request.Blocks.Count; i++)
        {
            var block = request.Blocks[i];
            sb.AppendLine($"  P{i + 1} (offsets {block.StartOffset}-{block.EndOffset}): {block.Text}");
        }
        sb.AppendLine();
        sb.AppendLine("Respond ONLY with valid JSON — an array of scene objects:");
        sb.AppendLine(@"[ { ""sceneIndex"": 0, ""blockIds"": [""P1""], ""focus"": ""brief scene focus"" }, ... ]");
        return sb.ToString();
    }

    private HttpRequestMessage BuildRequest(string payloadJson)
    {
        var url = VertexUrlResolver.Resolve(_vertex, _gemini, SemanticModel);
        var msg = new HttpRequestMessage(HttpMethod.Post, url);
        if (!_vertex.UseVertex)
        {
            msg.Headers.Add("x-goog-api-key", _gemini.ApiKey);
        }
        msg.Content = new StringContent(payloadJson, Encoding.UTF8, "application/json");
        return msg;
    }

    private static IReadOnlyList<SceneSelection> ParseResponse(string content)
    {
        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            if (!root.TryGetProperty("candidates", out var candidates) ||
                candidates.ValueKind != JsonValueKind.Array ||
                candidates.GetArrayLength() == 0)
                return Array.Empty<SceneSelection>();

            var text = candidates[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString() ?? "[]";

            using var scenesDoc = JsonDocument.Parse(text);
            var scenes = scenesDoc.RootElement;
            var result = new SceneSelection[scenes.GetArrayLength()];
            for (var i = 0; i < scenes.GetArrayLength(); i++)
            {
                var scene = scenes[i];
                var sceneIndex = scene.TryGetProperty("sceneIndex", out var si) ? si.GetInt32() : i;
                var blockIds = scene.TryGetProperty("blockIds", out var bi) && bi.ValueKind == JsonValueKind.Array
                    ? bi.EnumerateArray().Select(x => x.GetString()!).ToArray()
                    : Array.Empty<string>();
                var focus = scene.TryGetProperty("focus", out var f) ? f.GetString() : null;
                result[i] = new SceneSelection(sceneIndex, blockIds, focus);
            }
            return result;
        }
        catch
        {
            return Array.Empty<SceneSelection>();
        }
    }
}
