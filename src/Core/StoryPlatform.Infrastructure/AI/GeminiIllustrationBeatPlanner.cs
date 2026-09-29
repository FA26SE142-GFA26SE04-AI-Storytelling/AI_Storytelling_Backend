using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StoryPlatform.Application.Features.MediaGeneration.Interfaces;
using StoryPlatform.Application.Features.MediaGeneration.Models;

namespace StoryPlatform.Infrastructure.AI;

/// <summary>Suggests distinct visual moments; invalid/unavailable output is handled by the application fallback.</summary>
public sealed class GeminiIllustrationBeatPlanner : IIllustrationBeatPlanner
{
    private const string Model = "gemini-3.8-flash";
    private readonly HttpClient _httpClient;
    private readonly GeminiOptions _gemini;
    private readonly VertexOptions _vertex;
    private readonly ILogger<GeminiIllustrationBeatPlanner> _logger;

    public GeminiIllustrationBeatPlanner(HttpClient httpClient, IOptions<GeminiOptions> gemini,
        IOptions<VertexOptions> vertex, ILogger<GeminiIllustrationBeatPlanner> logger)
    {
        _httpClient = httpClient;
        _gemini = gemini.Value;
        _vertex = vertex.Value;
        _logger = logger;
        if (_httpClient.Timeout == Timeout.InfiniteTimeSpan || _httpClient.Timeout.TotalSeconds > _gemini.TimeoutSeconds)
            _httpClient.Timeout = TimeSpan.FromSeconds(_gemini.TimeoutSeconds);
    }

    public async Task<IReadOnlyList<IllustrationBeatSelection>> PlanAsync(
        IllustrationBeatPlanRequest request, CancellationToken cancellationToken = default)
    {
        if (!_vertex.UseVertex && string.IsNullOrWhiteSpace(_gemini.ApiKey)) return [];
        var prompt = new StringBuilder()
            .AppendLine("Plan 1 to 3 distinct illustrations for this children's story scene.")
            .AppendLine("Two illustrations may depict distinct moments within the SAME paragraph.")
            .AppendLine("For each beat, copy an EXACT nonempty excerpt from SceneText as anchorText. Do not change the text.")
            .AppendLine("Return ONLY a JSON array of objects: [{\"beatOrder\":1,\"anchorText\":\"exact excerpt\",\"visualFocus\":\"specific visible moment\"}].")
            .AppendLine("Use contiguous beatOrder starting at 1. Each focus must be specific and different.")
            .AppendLine("Media context (maintain characters, style, and child safety):")
            .AppendLine(request.MediaContextJson)
            .AppendLine("Scene visual description:").AppendLine(request.SceneVisualDescription)
            .AppendLine("SceneText:").Append(request.SceneText).ToString();
        var body = new { contents = new[] { new { role = "user", parts = new[] { new { text = prompt } } } } };
        using var message = new HttpRequestMessage(HttpMethod.Post, VertexUrlResolver.Resolve(_vertex, _gemini, Model));
        if (!_vertex.UseVertex) message.Headers.Add("x-goog-api-key", _gemini.ApiKey);
        message.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        try
        {
            using var response = await _httpClient.SendAsync(message, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Illustration beat planning HTTP {Status}", (int)response.StatusCode);
                return [];
            }
            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
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
            if (result.RootElement.ValueKind != JsonValueKind.Array) return [];
            var selections = new List<IllustrationBeatSelection>();
            var searchStart = 0;
            foreach (var beat in result.RootElement.EnumerateArray())
            {
                var anchor = beat.GetProperty("anchorText").GetString() ?? "";
                var start = string.IsNullOrWhiteSpace(anchor) ? -1 :
                    request.SceneText.IndexOf(anchor, searchStart, StringComparison.Ordinal);
                if (start < 0) return [];
                selections.Add(new IllustrationBeatSelection(beat.GetProperty("beatOrder").GetInt32(),
                    start, start + anchor.Length, beat.GetProperty("visualFocus").GetString() ?? ""));
                searchStart = start;
            }
            return selections;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Illustration beat planning unavailable; falling back to one beat");
            return [];
        }
    }
}
