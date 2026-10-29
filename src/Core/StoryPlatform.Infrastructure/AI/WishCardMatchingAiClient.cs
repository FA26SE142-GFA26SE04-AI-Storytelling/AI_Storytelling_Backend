using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using StoryPlatform.Application.Features.WishCards.Interfaces;
using StoryPlatform.Application.Features.WishCards.Services;
using StoryPlatform.Contracts.AI.WishCards;

namespace StoryPlatform.Infrastructure.AI;

public sealed class WishCardMatchingAiClient : IWishCardMatchingAiClient
{
    private readonly HttpClient _http;
    public WishCardMatchingAiClient(HttpClient http, IOptions<AIServiceOptions> options)
    {
        _http = http;
        _http.BaseAddress = new Uri(options.Value.BaseUrl.TrimEnd('/') + "/");
        _http.Timeout = TimeSpan.FromSeconds(Math.Clamp(options.Value.TimeoutSeconds, 10, 300));
        if (!string.IsNullOrWhiteSpace(options.Value.InternalApiKey))
            _http.DefaultRequestHeaders.Add("X-Internal-Api-Key", options.Value.InternalApiKey);
    }

    public async Task<MatchWishCardsResponse> MatchAsync(MatchWishCardsRequest request, CancellationToken ct)
    {
        WishCardMatchingRules.ValidateRequest(request);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_http.Timeout);
        var requestToken = timeout.Token;
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, "api/ai/wish-cards/match")
            { Content = JsonContent.Create(request) };
            using var response = await _http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, requestToken);
            // The contract permits 128 matches with two 500-character fields, including escaped Unicode.
            await response.Content.LoadIntoBufferAsync(1_000_000, requestToken);
            if (response.StatusCode == System.Net.HttpStatusCode.GatewayTimeout)
                throw new WishCardAiException("WISH_CARD_TIMEOUT", 504);
            if (!response.IsSuccessStatusCode)
            {
                try
                {
                    var error = await response.Content.ReadFromJsonAsync<ErrorBody>(cancellationToken: requestToken);
                    if (error?.ErrorCode == "WISH_CARD_OUTPUT_INVALID")
                        throw new WishCardAiException("WISH_CARD_OUTPUT_INVALID", 502);
                }
                catch (JsonException) { /* Do not expose upstream text. */ }
                throw new WishCardAiException("WISH_CARD_SERVICE_FAILED", 502);
            }
            var result = await response.Content.ReadFromJsonAsync<MatchWishCardsResponse>(cancellationToken: requestToken)
                ?? throw new JsonException("WISH_CARD_OUTPUT_INVALID");
            WishCardMatchingRules.ValidateResponse(request, result);
            return result;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new WishCardAiException("WISH_CARD_TIMEOUT", 504); }
        catch (JsonException) { throw new WishCardAiException("WISH_CARD_OUTPUT_INVALID", 502); }
        catch (HttpRequestException) { throw new WishCardAiException("WISH_CARD_SERVICE_FAILED", 502); }
    }
    private sealed record ErrorBody(string? ErrorCode);
}
