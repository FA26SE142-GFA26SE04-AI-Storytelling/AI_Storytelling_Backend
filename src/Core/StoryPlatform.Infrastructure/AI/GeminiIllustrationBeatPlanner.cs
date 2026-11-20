using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StoryPlatform.Application.Features.MediaGeneration;
using StoryPlatform.Application.Features.MediaGeneration.Interfaces;
using StoryPlatform.Application.Features.MediaGeneration.Models;
using StoryPlatform.Application.Features.MediaGeneration.Services;

namespace StoryPlatform.Infrastructure.AI;

/// <summary>Suggests distinct visual moments; invalid/unavailable output is handled by the application fallback.</summary>
public sealed class GeminiIllustrationBeatPlanner : IIllustrationBeatPlanner
{
    private const string Model = "gemini-3.8-flash";
    private const string OperationName = "IllustrationBeatPlanning";
    private readonly HttpClient _httpClient;
    private readonly GeminiOptions _gemini;
    private readonly VertexOptions _vertex;
    private readonly ILogger<GeminiIllustrationBeatPlanner> _logger;
    private readonly IMediaOutboundPrivacyGuard _privacyGuard;

    public GeminiIllustrationBeatPlanner(
        HttpClient httpClient,
        IOptions<GeminiOptions> gemini,
        IOptions<VertexOptions> vertex,
        ILogger<GeminiIllustrationBeatPlanner> logger,
        IMediaOutboundPrivacyGuard? privacyGuard = null)
    {
        _httpClient = httpClient;
        _gemini = gemini.Value;
        _vertex = vertex.Value;
        _logger = logger;
        _privacyGuard = privacyGuard ?? new DeterministicMediaOutboundPrivacyGuard();
        if (_httpClient.Timeout == Timeout.InfiniteTimeSpan || _httpClient.Timeout.TotalSeconds > _gemini.TimeoutSeconds)
            _httpClient.Timeout = TimeSpan.FromSeconds(_gemini.TimeoutSeconds);
    }

    public async Task<IReadOnlyList<IllustrationBeatSelection>> PlanAsync(
        IllustrationBeatPlanRequest request, CancellationToken cancellationToken = default)
    {
        var provider = _vertex.UseVertex ? "VertexAI" : "Gemini";
        var elapsed = Stopwatch.StartNew();
        var serializedContext = ProviderMediaContextSerializer.Serialize(request.MediaContext);
        var prompt = new StringBuilder()
            .AppendLine("Plan 1 to 3 distinct illustrations for this children's story scene.")
            .AppendLine("Two illustrations may depict distinct moments within the SAME paragraph.")
            .AppendLine("For each beat, copy an EXACT nonempty excerpt from SceneText as anchorText. Do not change the text.")
            .AppendLine("Return ONLY a JSON array of objects: [{\"beatOrder\":1,\"anchorText\":\"exact excerpt\",\"visualFocus\":\"specific visible moment\"}].")
            .AppendLine("Use contiguous beatOrder starting at 1. Each focus must be specific and different.")
            .AppendLine("Describe visible characters, actions and scenery only; never request captions, dialogue lettering or written story text in visualFocus.")
            .AppendLine("Media context (maintain characters, style, and child safety):")
            .AppendLine(serializedContext)
            .AppendLine("Scene visual description:").AppendLine(request.SceneVisualDescription)
            .AppendLine("SceneText:").Append(request.SceneText).ToString();

        try
        {
            _privacyGuard.EnsureSafeForProvider(OperationName, prompt);
        }
        catch (MediaProviderPrivacyException ex)
        {
            MediaProviderDiagnostics.LogFailed(
                _logger, OperationName, provider, Model, elapsed.Elapsed.TotalMilliseconds,
                null, ex.ErrorCode, null, null, ex.GetType().Name);
            throw;
        }

        if (!_vertex.UseVertex && string.IsNullOrWhiteSpace(_gemini.ApiKey)) return [];

        var body = new { contents = new[] { new { role = "user", parts = new[] { new { text = prompt } } } } };
        var payloadJson = JsonSerializer.Serialize(body);
        var requestBytes = Encoding.UTF8.GetByteCount(payloadJson);
        MediaProviderDiagnostics.LogStarted(_logger, OperationName, provider, Model, prompt.Length, requestBytes);

        using var message = new HttpRequestMessage(HttpMethod.Post, VertexUrlResolver.Resolve(_vertex, _gemini, Model));
        if (!_vertex.UseVertex) message.Headers.Add("x-goog-api-key", _gemini.ApiKey);
        message.Content = new StringContent(payloadJson, Encoding.UTF8, "application/json");
        try
        {
            using var response = await _httpClient.SendAsync(message, cancellationToken).ConfigureAwait(false);
            var providerRequestId = MediaProviderDiagnostics.ExtractAllowlistedRequestId(response);
            if (!response.IsSuccessStatusCode)
            {
                var errorCode = $"GEMINI_BEAT_PLANNER_HTTP_{(int)response.StatusCode}";
                MediaProviderDiagnostics.LogFailed(
                    _logger, OperationName, provider, Model, elapsed.Elapsed.TotalMilliseconds,
                    (int)response.StatusCode, errorCode, MediaProviderDiagnostics.GetRetryAfterSeconds(response),
                    providerRequestId, nameof(HttpRequestException));
                return [];
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var responseBytes = Encoding.UTF8.GetByteCount(json);
            var usage = MediaProviderDiagnostics.ExtractUsageMetadata(json);

            using var envelope = JsonDocument.Parse(json);
            var parts = envelope.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts");
            var answer = parts.EnumerateArray().First(x => x.TryGetProperty("text", out _)).GetProperty("text").GetString() ?? "";
            answer = answer.Trim();
            if (answer.StartsWith("```", StringComparison.Ordinal))
            {
                var firstNewline = answer.IndexOf('\n');
                answer = firstNewline < 0 ? "" : answer[(firstNewline + 1)..];
                if (answer.EndsWith("```", StringComparison.Ordinal)) answer = answer[..^3].Trim();
            }
            using var result = JsonDocument.Parse(answer);
            if (result.RootElement.ValueKind != JsonValueKind.Array)
            {
                MediaProviderDiagnostics.LogCompleted(
                    _logger, OperationName, provider, Model, elapsed.Elapsed.TotalMilliseconds,
                    (int)response.StatusCode, providerRequestId, responseBytes, "application/json", usage);
                return [];
            }
            var selections = new List<IllustrationBeatSelection>();
            var searchStart = 0;
            foreach (var beat in result.RootElement.EnumerateArray())
            {
                var anchor = beat.GetProperty("anchorText").GetString() ?? "";
                var start = string.IsNullOrWhiteSpace(anchor) ? -1 :
                    request.SceneText.IndexOf(anchor, searchStart, StringComparison.Ordinal);
                if (start < 0)
                {
                    MediaProviderDiagnostics.LogCompleted(
                        _logger, OperationName, provider, Model, elapsed.Elapsed.TotalMilliseconds,
                        (int)response.StatusCode, providerRequestId, responseBytes, "application/json", usage);
                    return [];
                }
                selections.Add(new IllustrationBeatSelection(beat.GetProperty("beatOrder").GetInt32(),
                    start, start + anchor.Length, beat.GetProperty("visualFocus").GetString() ?? ""));
                searchStart = start;
            }
            MediaProviderDiagnostics.LogCompleted(
                _logger, OperationName, provider, Model, elapsed.Elapsed.TotalMilliseconds,
                (int)response.StatusCode, providerRequestId, responseBytes, "application/json", usage);
            return selections;
        }
        catch (OperationCanceledException)
        {
            MediaProviderDiagnostics.LogCancelled(
                _logger, OperationName, provider, Model, elapsed.Elapsed.TotalMilliseconds,
                cancellationToken.IsCancellationRequested);
            if (cancellationToken.IsCancellationRequested) throw;
            return [];
        }
        catch (MediaProviderContextException) { throw; }
        catch (MediaProviderPrivacyException) { throw; }
        catch (Exception exception)
        {
            MediaProviderDiagnostics.LogFailed(
                _logger, OperationName, provider, Model, elapsed.Elapsed.TotalMilliseconds,
                null, "GEMINI_BEAT_PLANNER_UNAVAILABLE", null, null, exception.GetType().Name);
            return [];
        }
    }
}
