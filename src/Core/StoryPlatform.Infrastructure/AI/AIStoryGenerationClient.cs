using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using StoryPlatform.Application.Abstractions.AI;
using StoryPlatform.Contracts.AI.Requests;
using StoryPlatform.Contracts.AI.Responses;

namespace StoryPlatform.Infrastructure.AI;

public sealed class AIStoryGenerationClient : IAIStoryGenerationClient
{
    private const string ApiKeyHeader = "X-Internal-Api-Key";
    private readonly HttpClient _httpClient;

    public AIStoryGenerationClient(HttpClient httpClient, IOptions<AIServiceOptions> options)
    {
        var settings = options.Value;
        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri(settings.BaseUrl.TrimEnd('/') + '/');
        _httpClient.Timeout = TimeSpan.FromSeconds(Math.Clamp(settings.TimeoutSeconds, 10, 300));
        if (!string.IsNullOrWhiteSpace(settings.InternalApiKey))
        {
            _httpClient.DefaultRequestHeaders.Add(ApiKeyHeader, settings.InternalApiKey);
        }
    }

    public Task<GenerateOutlineResponse> GenerateOutlineAsync(GenerateOutlineRequest request, CancellationToken cancellationToken = default) =>
        PostAsync<GenerateOutlineRequest, GenerateOutlineResponse>("api/ai/outline", request, cancellationToken);

    public Task<GenerateStoryResponse> GenerateStoryAsync(GenerateStoryRequest request, CancellationToken cancellationToken = default) =>
        PostAsync<GenerateStoryRequest, GenerateStoryResponse>("api/ai/story", request, cancellationToken);

    public Task<RefineStoryResponse> RefineStoryAsync(RefineStoryRequest request, CancellationToken cancellationToken = default) =>
        PostAsync<RefineStoryRequest, RefineStoryResponse>("api/ai/refine", request, cancellationToken);

    public Task<EvaluateStoryResponse> EvaluateStoryAsync(EvaluateStoryRequest request, CancellationToken cancellationToken = default) =>
        PostAsync<EvaluateStoryRequest, EvaluateStoryResponse>("api/ai/evaluate", request, cancellationToken);

    private async Task<TResponse> PostAsync<TRequest, TResponse>(
        string path,
        TRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(path, request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken: cancellationToken)
               ?? throw new InvalidOperationException($"AI service returned an empty response for '{path}'.");
    }
}
