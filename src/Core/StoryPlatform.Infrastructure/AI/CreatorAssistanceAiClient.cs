using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using StoryPlatform.Application.Features.CreatorAssistance.Interfaces;
using StoryPlatform.Contracts.AI.CreatorAssistance;

namespace StoryPlatform.Infrastructure.AI;

public sealed class CreatorAssistanceAiClient : ICreatorAssistanceAiClient
{
    private readonly HttpClient _http;
    public CreatorAssistanceAiClient(HttpClient http, IOptions<AIServiceOptions> options)
    {
        _http = http;
        _http.BaseAddress = new Uri(options.Value.BaseUrl.TrimEnd('/') + "/");
        _http.Timeout = Timeout.InfiniteTimeSpan; // Application owns the bounded timeout and persisted operation lifecycle.
        if (!string.IsNullOrWhiteSpace(options.Value.InternalApiKey)) _http.DefaultRequestHeaders.Add("X-Internal-Api-Key", options.Value.InternalApiKey);
    }
    public async Task<AssistanceAiResult> GenerateAsync(AssistanceAiRequest request, CancellationToken ct = default)
    {
        using var response = await _http.PostAsJsonAsync("api/ai/creator-assistance", request, ct);
        if (response.Content.Headers.ContentLength > 100_000) throw new JsonException("AI_OUTPUT_INVALID");
        await response.Content.LoadIntoBufferAsync(100_000, ct);
        if (!response.IsSuccessStatusCode)
        {
            try
            {
                var error = await response.Content.ReadFromJsonAsync<ErrorBody>(cancellationToken: ct);
                if (error?.ErrorCode is "AI_OUTPUT_INVALID" or "AI_OUTPUT_REJECTED" or "AI_TIMEOUT" or "AI_SERVICE_UNAVAILABLE")
                    throw new StoryPlatform.Application.Features.CreatorAssistance.AssistanceException(error.ErrorCode, error.ErrorCode == "AI_TIMEOUT" ? 504 : 502);
            }
            catch (JsonException) { /* Never forward an upstream body or message. */ }
            throw new HttpRequestException("Creator assistance service unavailable.", null, response.StatusCode);
        }
        return await response.Content.ReadFromJsonAsync<AssistanceAiResult>(cancellationToken: ct) ?? throw new JsonException("AI_OUTPUT_INVALID");
    }
    private sealed record ErrorBody(string? ErrorCode);
}
