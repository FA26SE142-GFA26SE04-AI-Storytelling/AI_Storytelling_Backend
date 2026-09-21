using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StoryPlatform.Application.Features.MediaGeneration.Interfaces;
using StoryPlatform.Application.Features.MediaGeneration.Models;
using StoryPlatform.Infrastructure.AI;

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
    private const string SemanticModel = "gemini-2.5-flash";

    public GeminiSemanticSceneSegmentationProvider(
        HttpClient httpClient,
        IOptions<GeminiOptions> geminiOptions,
        IOptions<VertexOptions> vertexOptions,
        ILogger<GeminiSemanticSceneSegmentationProvider> logger)
    {
        _httpClient = httpClient;
        _gemini = geminiOptions.Value;
        _vertex = vertexOptions.Value;
        _logger = logger;
        if (_httpClient.Timeout == Timeout.InfiniteTimeSpan || _httpClient.Timeout.TotalSeconds > _gemini.TimeoutSeconds)
            _httpClient.Timeout = TimeSpan.FromSeconds(_gemini.TimeoutSeconds);
    }

    public async Task<IReadOnlyList<SceneSelection>> SegmentAsync(
        SceneSegmentationRequest request, CancellationToken cancellationToken = default)
    {
        if (!_vertex.UseVertex && string.IsNullOrWhiteSpace(_gemini.ApiKey))
        {
            _logger.LogWarning("Gemini API key not configured and Vertex is disabled, returning empty — orchestrator will fallback");
            return Array.Empty<SceneSelection>();
        }

        var prompt = BuildPrompt(request);
        var body = new { contents = new[] { new { role = "user", parts = new[] { new { text = prompt } } } } };
        using var response = await GeminiHttpRetry.SendWithTransportRetryAsync(
            () => BuildRequest(body),
            _httpClient,
            _gemini.TransportRetryCount,
            TimeSpan.FromMilliseconds(_gemini.TransportRetryBaseDelayMs),
            _logger,
            cancellationToken).ConfigureAwait(false);

        var statusCode = response.StatusCode;
        var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!IsSuccess(statusCode))
        {
            _logger.LogWarning("Gemini semantic segmentation HTTP {Status}: {Body}", (int)statusCode, Truncate(content, 300));
            return Array.Empty<SceneSelection>();
        }
        return ParseResponse(content);
    }

    private static bool IsSuccess(System.Net.HttpStatusCode code)
    {
        var n = (int)code;
        return n >= 200 && n < 300;
    }

    private static string Truncate(string value, int max) =>
        string.IsNullOrEmpty(value) ? string.Empty :
        value.Length <= max ? value : value[..max];

    private static string BuildPrompt(SceneSegmentationRequest request)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are a story scene segmentation assistant for children's stories.");
        sb.AppendLine("Given the following story paragraphs and context, decide how to group them into coherent scenes for illustration.");
        sb.AppendLine();
        sb.AppendLine("CONTEXT:");
        sb.AppendLine(request.MediaContextJson);
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

    private HttpRequestMessage BuildRequest(object body)
    {
        var url = _vertex.UseVertex
            ? $"https://{_vertex.Location}-aiplatform.googleapis.com/v1/projects/{_vertex.ProjectId}/locations/{_vertex.Location}/publishers/google/models/{SemanticModel}:generateContent"
            : $"{_gemini.Endpoint.TrimEnd('/')}/{SemanticModel}:generateContent";

        var msg = new HttpRequestMessage(HttpMethod.Post, url);
        if (!_vertex.UseVertex)
        {
            msg.Headers.Add("x-goog-api-key", _gemini.ApiKey);
        }
        msg.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
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
