using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
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
        var baseUrl = !string.IsNullOrWhiteSpace(settings.BaseUrl) ? settings.BaseUrl : "http://localhost:5260";
        _httpClient.BaseAddress = new Uri(baseUrl.TrimEnd('/') + '/');
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

    public Task<GenerateStoryContentResponse> GenerateStoryContentAsync(GenerateStoryContentRequest request, CancellationToken cancellationToken = default) =>
        PostAsync<GenerateStoryContentRequest, GenerateStoryContentResponse>("api/ai/story-content/content", request, cancellationToken);

    public Task<RefineStoryContentResponse> RefineStoryContentAsync(RefineStoryContentRequest request, CancellationToken cancellationToken = default) =>
        PostAsync<RefineStoryContentRequest, RefineStoryContentResponse>("api/ai/story-content/content/refine", request, cancellationToken);

    public Task<GenerateVocabularyResponse> GenerateVocabularyAsync(GenerateVocabularyRequest request, CancellationToken cancellationToken = default) =>
        PostAsync<GenerateVocabularyRequest, GenerateVocabularyResponse>("api/ai/story-content/vocabulary", request, cancellationToken);

    public Task<GenerateQuizResponse> GenerateQuizAsync(GenerateQuizRequest request, CancellationToken cancellationToken = default) =>
        PostAsync<GenerateQuizRequest, GenerateQuizResponse>("api/ai/story-content/quiz", request, cancellationToken);

    public Task<GenerateDiscussionResponse> GenerateDiscussionAsync(GenerateDiscussionRequest request, CancellationToken cancellationToken = default) =>
        PostAsync<GenerateDiscussionRequest, GenerateDiscussionResponse>("api/ai/story-content/discussion", request, cancellationToken);

    public Task<EvaluateContentSafetyResponse> EvaluateContentSafetyAsync(EvaluateContentSafetyRequest request, CancellationToken cancellationToken = default) =>
        PostAsync<EvaluateContentSafetyRequest, EvaluateContentSafetyResponse>("api/ai/story-content/content/safety", request, cancellationToken);

    private async Task<TResponse> PostAsync<TRequest, TResponse>(
        string path,
        TRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(path, request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            AIErrorResponse? error = null;
            try
            {
                error = await response.Content.ReadFromJsonAsync<AIErrorResponse>(cancellationToken: cancellationToken);
            }
            catch (JsonException)
            {
                // Preserve a stable Core-side error when an upstream proxy returns a non-JSON body.
            }

            throw new AIServiceRequestException(
                (int)response.StatusCode,
                string.IsNullOrWhiteSpace(error?.ErrorCode) ? "AI_SERVICE_REQUEST_FAILED" : error.ErrorCode,
                string.IsNullOrWhiteSpace(error?.Error) ? "AI service request failed." : error.Error);
        }
        return await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken: cancellationToken)
               ?? throw new InvalidOperationException($"AI service returned an empty response for '{path}'.");
    }

    private sealed record AIErrorResponse(string? ErrorCode, string? Error);
}
