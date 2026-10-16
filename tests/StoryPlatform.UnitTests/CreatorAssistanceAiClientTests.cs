using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using StoryPlatform.Application.Features.CreatorAssistance;
using StoryPlatform.Contracts.AI.CreatorAssistance;
using StoryPlatform.Infrastructure.AI;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed class CreatorAssistanceAiClientTests
{
    [Theory]
    [InlineData(504, "AI_TIMEOUT", 504)]
    [InlineData(503, "AI_SERVICE_UNAVAILABLE", 502)]
    [InlineData(502, "AI_OUTPUT_INVALID", 502)]
    [InlineData(502, "AI_OUTPUT_REJECTED", 502)]
    public async Task KnownUpstreamErrorKeepsStableCode(int upstreamStatus, string code, int expectedStatus)
    {
        using var http = new HttpClient(new ResponseHandler(upstreamStatus, "{\"errorCode\":\"" + code + "\",\"error\":\"private content\"}"));
        var client = new CreatorAssistanceAiClient(http, Options.Create(new AIServiceOptions { BaseUrl = "http://localhost" }));
        var error = await Assert.ThrowsAsync<AssistanceException>(() => client.GenerateAsync(new AssistanceAiRequest()));
        Assert.Equal(code, error.Code); Assert.Equal(expectedStatus, error.StatusCode);
        Assert.DoesNotContain("private content", error.Message);
    }

    [Fact]
    public async Task UnknownProviderBodyIsNotForwarded()
    {
        using var http = new HttpClient(new ResponseHandler(500, "{\"errorCode\":\"private content\",\"error\":\"private content\"}"));
        var client = new CreatorAssistanceAiClient(http, Options.Create(new AIServiceOptions { BaseUrl = "http://localhost" }));
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => client.GenerateAsync(new AssistanceAiRequest()));
        Assert.DoesNotContain("private content", error.Message);
    }

    private sealed class ResponseHandler(int status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }
}
