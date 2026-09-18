using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using StoryPlatform.Application.Abstractions.AI;
using StoryPlatform.Contracts.AI.Requests;
using StoryPlatform.Infrastructure.AI;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed class AIStoryGenerationClientTests
{
    [Fact]
    public async Task GenerateOutline_with_valid_response_returns_outline()
    {
        // Arrange
        var handler = new StubHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                {
                    "candidates": [{
                        "content": {
                            "parts": [{
                                "text": "{\"title\":\"Test Story\",\"opening\":\"Once upon a time\",\"development\":\"Something happened\",\"ending\":\"The end\"}"
                            }]
                        }
                    }]
                }
                """,
                Encoding.UTF8,
                "application/json")
        });

        var client = new GeminiDirectClient(
            new HttpClient(handler),
            Options.Create(new AIServiceOptions { ApiKey = "test-key" }));

        // Act
        var result = await client.GenerateOutlineAsync(new GenerateOutlineRequest
        {
            RequestId = "request-1",
            AgeBand = "6-8",
            Language = "vi"
        });

        // Assert
        Assert.Equal("request-1", result.RequestId);
        Assert.Equal("Test Story", result.Title);
        Assert.Equal("Once upon a time", result.Outline.Opening);
    }

    [Fact]
    public async Task GenerateOutline_with_invalid_api_key_throws_exception()
    {
        // Arrange
        var handler = new StubHandler(new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent(
                """{"error": "Invalid API key"}""",
                Encoding.UTF8,
                "application/json")
        });

        var client = new GeminiDirectClient(
            new HttpClient(handler),
            Options.Create(new AIServiceOptions { ApiKey = "invalid-key" }));

        // Act & Assert
        await Assert.ThrowsAsync<AIServiceRequestException>(() =>
            client.GenerateOutlineAsync(new GenerateOutlineRequest
            {
                RequestId = "request-1",
                AgeBand = "6-8",
                Language = "vi"
            }));
    }

    private sealed class StubHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(response);
    }
}
