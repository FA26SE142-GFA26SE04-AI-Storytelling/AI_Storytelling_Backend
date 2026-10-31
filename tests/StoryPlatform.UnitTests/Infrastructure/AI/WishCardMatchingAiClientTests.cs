using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using StoryPlatform.Application.Features.WishCards.Services;
using StoryPlatform.Contracts.AI.WishCards;
using StoryPlatform.Infrastructure.AI;
using Xunit;

namespace StoryPlatform.UnitTests.Infrastructure.AI;

public sealed class WishCardMatchingAiClientTests
{
    [Fact]
    public async Task Sends_authenticated_contract_to_AI_and_validates_response()
    {
        var request = Request();
        var transport = new Transport(async message =>
        {
            Assert.Equal("/api/ai/wish-cards/match", message.RequestUri!.AbsolutePath);
            Assert.Equal(HttpMethod.Post, message.Method);
            Assert.Equal("test-only-key", Assert.Single(message.Headers.GetValues("X-Internal-Api-Key")));
            var body = await message.Content!.ReadFromJsonAsync<MatchWishCardsRequest>();
            Assert.Equal(request.SourceFingerprint, body!.SourceFingerprint);
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new MatchWishCardsResponse
            {
                RequestId = request.RequestId, SourceFingerprint = request.SourceFingerprint,
                Model = "fixture", Matches = [new(1, "A cat", "A character")]
            }) };
        });
        var response = await Client(transport).MatchAsync(request, default);
        Assert.Equal(1, Assert.Single(response.Matches).CardId);
    }

    [Theory]
    [InlineData(401, 502, "WISH_CARD_SERVICE_FAILED")]
    [InlineData(429, 502, "WISH_CARD_SERVICE_FAILED")]
    [InlineData(504, 504, "WISH_CARD_TIMEOUT")]
    public async Task Maps_upstream_failures_without_exposing_raw_messages(int upstream, int expectedStatus, string code)
    {
        var client = Client(new Transport(_ => Task.FromResult(new HttpResponseMessage((HttpStatusCode)upstream)
        { Content = new StringContent("private provider text") })));
        var error = await Assert.ThrowsAsync<WishCardAiException>(() => client.MatchAsync(Request(), default));
        Assert.Equal(expectedStatus, error.StatusCode);
        Assert.Equal(code, error.Message);
    }

    [Fact]
    public async Task Timeout_also_bounds_the_response_body_after_headers_have_arrived()
    {
        var transport = new Transport(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new WaitingContent() }));
        using var http = new HttpClient(transport);
        var client = new WishCardMatchingAiClient(http, Options.Create(new AIServiceOptions { BaseUrl = "http://ai.test" }));
        http.Timeout = TimeSpan.FromMilliseconds(50);
        var error = await Assert.ThrowsAsync<WishCardAiException>(() => client.MatchAsync(Request(), default));
        Assert.Equal(504, error.StatusCode);
        Assert.Equal("WISH_CARD_TIMEOUT", error.Message);
    }

    [Fact]
    public async Task Rejects_a_response_for_another_snapshot()
    {
        var request = Request();
        var client = Client(new Transport(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new MatchWishCardsResponse
            { RequestId = request.RequestId, SourceFingerprint = "different", Model = "fixture", Matches = [] })
        })));
        var error = await Assert.ThrowsAsync<WishCardAiException>(() => client.MatchAsync(request, default));
        Assert.Equal("WISH_CARD_OUTPUT_INVALID", error.Message);
    }

    private static WishCardMatchingAiClient Client(Transport transport) => new(new HttpClient(transport),
        Options.Create(new AIServiceOptions { BaseUrl = "http://ai.test", InternalApiKey = "test-only-key" }));
    private static MatchWishCardsRequest Request()
    {
        var request = new MatchWishCardsRequest { RequestId = "test-matching", Title = "Cat", Content = "A cat walked.",
            AgeBand = "6-8", Language = "en", Candidates = [new(1, "Character", "cat")] };
        return request with { SourceFingerprint = WishCardMatchingRules.Fingerprint(request) };
    }
    private sealed class Transport(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => respond(request);
    }
    private sealed class WaitingContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            throw new InvalidOperationException("The cancellable content API must be used.");
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken ct) =>
            Task.Delay(Timeout.Infinite, ct);
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
    }
}
