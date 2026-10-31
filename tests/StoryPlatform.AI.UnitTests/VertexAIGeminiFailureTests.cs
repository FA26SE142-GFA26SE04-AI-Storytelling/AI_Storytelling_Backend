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
    [Fact]
    public async Task Wish_matching_wire_schema_avoids_large_array_bound_and_preserves_evidence_fields()
    {
        using var transport = new Transport(HttpStatusCode.OK, """{"candidates":[{"finishReason":"STOP","content":{"parts":[{"text":"{\"matches\":[]}"}]}}]}""");
        using var sdk = CreateSdk(transport);
        var client = CreateClient(sdk);
        var handler = new StoryPlatform.AI.Application.WishCards.MatchWishCardsHandler(client,
            Options.Create(new StoryPlatform.AI.Application.WishCards.WishCardMatchingOptions()));
        var request = new StoryPlatform.Contracts.AI.WishCards.MatchWishCardsRequest
        {
            RequestId = "wire-test", Title = "Cat", Content = "A cat walked.", AgeBand = "6-8", Language = "en",
            Candidates = [new(1, "Character", "cat")]
        };
        request = request with { SourceFingerprint = StoryPlatform.Contracts.AI.WishCards.WishCardMatchingRules.Fingerprint(request) };
        await handler.HandleAsync(request);
        using var wire = JsonDocument.Parse(transport.RequestBody!);
        var config = wire.RootElement.GetProperty("generationConfig");
        Assert.Equal("application/json", config.GetProperty("responseMimeType").GetString());
        var matches = config.GetProperty("responseSchema").GetProperty("properties").GetProperty("matches");
        Assert.False(matches.TryGetProperty("maxItems", out _));
        var item = matches.GetProperty("items");
        Assert.Equal(new[] { "cardId", "sourceQuote", "reason" },
            item.GetProperty("required").EnumerateArray().Select(x => x.GetString()).ToArray());
        Assert.False(item.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(1, transport.Calls);
    }

    [Fact]
    public void Provider_diagnostic_never_returns_arbitrary_provider_text()
    {
        var result = VertexAIGeminiClient.ClassifyProviderDiagnostic("private-story secret-key responseSchema INVALID_ARGUMENT");
        Assert.Equal("responseSchema,INVALID_ARGUMENT", result);
        Assert.Equal("UNCLASSIFIED", VertexAIGeminiClient.ClassifyProviderDiagnostic("private-story secret-key"));
    }

    [Fact]
    public async Task Wish_matching_provider_bad_request_is_not_retried_or_exposed()
    {
        using var transport = new Transport(HttpStatusCode.BadRequest,
            """{"error":{"code":400,"message":"private-story secret-key responseSchema","status":"INVALID_ARGUMENT"}}""");
        using var sdk = CreateSdk(transport);
        var client = CreateClient(sdk);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var middleware = new AIExceptionHandlingMiddleware(async _ =>
            { await client.GenerateStructuredAsync("private-story", "wish_card_matching", Schema); },
            NullLogger<AIExceptionHandlingMiddleware>.Instance);
        await middleware.InvokeAsync(context);
        Assert.Equal(1, transport.Calls);
        Assert.Equal(400, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        using var body = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal("AI_PROVIDER_CONFIGURATION_REJECTED", body.RootElement.GetProperty("errorCode").GetString());
        Assert.DoesNotContain("private-story", body.RootElement.GetRawText());
        Assert.DoesNotContain("secret-key", body.RootElement.GetRawText());
    }
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
    [InlineData("wish_card_matching", true)]
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
        public string? RequestBody { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            RequestBody = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        }
    }
}
