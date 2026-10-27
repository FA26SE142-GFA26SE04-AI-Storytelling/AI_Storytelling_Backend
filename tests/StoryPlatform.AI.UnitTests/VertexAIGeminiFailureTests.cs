using System.Net;
using System.Text;
using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using Google.GenAI;
using Google.GenAI.Types;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StoryPlatform.AI.Api.Middleware;
using StoryPlatform.AI.Domain.Generation;
using StoryPlatform.AI.Infrastructure.LLM.VertexAI;
using Xunit;

namespace StoryPlatform.AI.UnitTests;

public sealed class VertexAIGeminiFailureTests
{
    private static readonly JsonElement Schema = JsonSerializer.SerializeToElement(new { type = "object" });

    [Theory]
    [InlineData("learning_quiz")]
    [InlineData("learning_vocabulary")]
    [InlineData("story_content")]
    public async Task Max_tokens_survives_vertex_error_handling_and_reaches_middleware(string schemaName)
    {
        using var transport = new Transport(HttpStatusCode.OK, """
            {"candidates":[{"finishReason":"MAX_TOKENS","content":{"role":"model","parts":[{"text":"private partial output"}]}}]}
            """);
        using var sdk = CreateSdk(transport);
        var client = CreateClient(sdk);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        async Task Invoke(HttpContext _)
        {
            var error = await Assert.ThrowsAsync<LlmOutputTruncatedException>(() =>
                client.GenerateStructuredAsync("private story", schemaName, Schema));
            throw error;
        }
        var middleware = new AIExceptionHandlingMiddleware(Invoke, NullLogger<AIExceptionHandlingMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.Equal(1, transport.Calls);
        Assert.Equal(502, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        using var body = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal("AI_OUTPUT_TRUNCATED", body.RootElement.GetProperty("errorCode").GetString());
        Assert.DoesNotContain("private", body.RootElement.GetRawText());
    }

    [Theory]
    [InlineData("story_content", true)]
    [InlineData("refined_story_content", true)]
    [InlineData("learning_quiz", true)]
    [InlineData("story_content", false)]
    public async Task Rate_limit_preserves_provider_delay_through_sdk_and_middleware(string schemaName, bool hasHint)
    {
        var details = hasHint ? """, "details":[{"@type":"type.googleapis.com/google.rpc.RetryInfo","retryDelay":"90.5s"}]""" : "";
        using var transport = new Transport(HttpStatusCode.TooManyRequests,
            "{\"error\":{\"code\":429,\"message\":\"private provider details\",\"status\":\"RESOURCE_EXHAUSTED\"" + details + "}}");
        using var sdk = CreateSdk(transport);
        var client = CreateClient(sdk);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        async Task Invoke(HttpContext _)
        {
            var error = await Assert.ThrowsAsync<HttpRequestException>(() =>
                client.GenerateStructuredAsync("private story", schemaName, Schema));
            Assert.Equal(HttpStatusCode.TooManyRequests, error.StatusCode);
            if (hasHint) Assert.Equal(90.5, error.Data[VertexAIGeminiClient.RetryAfterSecondsKey]);
            else Assert.Null(error.Data[VertexAIGeminiClient.RetryAfterSecondsKey]);
            throw error;
        }
        var middleware = new AIExceptionHandlingMiddleware(Invoke, NullLogger<AIExceptionHandlingMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.Equal(1, transport.Calls);
        Assert.Equal(429, context.Response.StatusCode);
        Assert.Equal(hasHint ? "91" : "", context.Response.Headers.RetryAfter.ToString());
        context.Response.Body.Position = 0;
        using var body = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal("AI_PROVIDER_429", body.RootElement.GetProperty("errorCode").GetString());
        Assert.DoesNotContain("private", body.RootElement.GetRawText());
    }

    private static VertexAIGeminiClient CreateClient(Client sdk) => new(Options.Create(new VertexAIOptions
    {
        AuthMode = "Adc", ProjectId = "offline-test-project", Location = "global", Model = "test-model"
    }), NullLogger<VertexAIGeminiClient>.Instance, sdk);

    private static Client CreateSdk(Transport transport) => new(vertexAI: true,
        project: "offline-test-project", location: "global", credential: GoogleCredential.FromAccessToken("offline-test-token"),
        httpOptions: new HttpOptions { RetryOptions = new HttpRetryOptions { Attempts = 1 } },
        clientOptions: new ClientOptions { HttpClientFactory = () => new HttpClient(transport, disposeHandler: false) });

    private sealed class Transport(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}
