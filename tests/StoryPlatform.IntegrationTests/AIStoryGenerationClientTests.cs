using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using StoryPlatform.Contracts.AI.Requests;
using StoryPlatform.Infrastructure.AI;
using Xunit;

namespace StoryPlatform.IntegrationTests;

public sealed class AIStoryGenerationClientTests
{
    [Fact]
    public async Task GenerateOutline_UsesInternalEndpointAndDeserializesContract()
    {
        var handler = new StubHandler("""
            {"requestId":"req-1","generationId":"gen-1","title":"A safe story","outline":{"opening":"A","development":"B","ending":"C"},"metadata":{}}
            """);
        var client = new AIStoryGenerationClient(
            new HttpClient(handler),
            Options.Create(new AIServiceOptions { BaseUrl = "http://ai-service", InternalApiKey = "test-key" }));

        var response = await client.GenerateOutlineAsync(new GenerateOutlineRequest { RequestId = "req-1" });

        Assert.Equal("A safe story", response.Title);
        Assert.Equal("/api/ai/outline", handler.RequestUri?.AbsolutePath);
        Assert.Equal("test-key", handler.ApiKey);
    }

    private sealed class StubHandler(string responseBody) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public string? ApiKey { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            ApiKey = request.Headers.GetValues("X-Internal-Api-Key").Single();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            });
        }
    }
}
