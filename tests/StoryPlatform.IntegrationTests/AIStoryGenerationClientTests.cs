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
    public async Task GenerateOutline_DeserializesGeminiResponse()
    {
        // Arrange - Gemini API response format
        var handler = new StubHandler("""
            {
                "candidates": [{
                    "content": {
                        "parts": [{
                            "text": "{\"title\":\"A safe story\",\"opening\":\"A\",\"development\":\"B\",\"ending\":\"C\"}"
                        }]
                    }
                }]
            }
            """);
        var client = new GeminiDirectClient(
            new HttpClient(handler),
            Options.Create(new AIServiceOptions { ApiKey = "test-key", Model = "gemini-2.5-flash" }));

        // Act
        var response = await client.GenerateOutlineAsync(new GenerateOutlineRequest
        {
            RequestId = "req-1",
            AgeBand = "6-8",
            Language = "vi"
        });

        // Assert
        Assert.Equal("A safe story", response.Title);
        Assert.Equal("A", response.Outline.Opening);
        Assert.Equal("B", response.Outline.Development);
        Assert.Equal("C", response.Outline.Ending);
    }

    [Fact]
    public async Task GenerateStory_DeserializesGeminiResponse()
    {
        // Arrange - Gemini API response format
        var handler = new StubHandler("""
            {
                "candidates": [{
                    "content": {
                        "parts": [{
                            "text": "{\"title\":\"My Story\",\"storySections\":[{\"order\":1,\"heading\":\"Chapter 1\",\"content\":\"Content here\"}],\"lesson\":\"Be kind\"}"
                        }]
                    }
                }]
            }
            """);
        var client = new GeminiDirectClient(
            new HttpClient(handler),
            Options.Create(new AIServiceOptions { ApiKey = "test-key", Model = "gemini-2.5-flash" }));

        // Act
        var response = await client.GenerateStoryAsync(new GenerateStoryRequest
        {
            RequestId = "req-1",
            AgeBand = "6-8",
            Language = "vi",
            Outline = new StoryPlatform.Contracts.AI.Models.StoryOutlineDto("A", "B", "C")
        });

        // Assert
        Assert.Equal("My Story", response.Story.Title);
        Assert.Single(response.Story.StorySections);
        Assert.Equal("Chapter 1", response.Story.StorySections[0].Heading);
        Assert.Equal("Be kind", response.Story.Lesson);
    }

    private sealed class StubHandler(string responseBody) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public string? ApiKey { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            });
        }
    }
}
