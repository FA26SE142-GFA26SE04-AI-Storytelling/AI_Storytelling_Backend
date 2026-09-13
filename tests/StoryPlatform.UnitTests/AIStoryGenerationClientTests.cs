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
    public async Task Outline_safety_error_preserves_reason_code()
    {
        var handler = new StubHandler(new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
        {
            Content = new StringContent(
                """{"errorCode":"OUTLINE_BLOCKED_CONTENT","error":"Generated outline was blocked."}""",
                Encoding.UTF8,
                "application/json")
        });
        var client = new AIStoryGenerationClient(
            new HttpClient(handler),
            Options.Create(new AIServiceOptions { BaseUrl = "http://localhost" }));

        var exception = await Assert.ThrowsAsync<AIServiceRequestException>(() =>
            client.GenerateOutlineAsync(new GenerateOutlineRequest { RequestId = "request-1" }));

        Assert.Equal(422, exception.StatusCode);
        Assert.Equal("OUTLINE_BLOCKED_CONTENT", exception.ErrorCode);
    }

    private sealed class StubHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(response);
    }
}
