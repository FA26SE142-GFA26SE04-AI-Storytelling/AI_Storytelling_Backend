using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using StoryPlatform.AI.Infrastructure.LLM.Gemini;
using Xunit;

namespace StoryPlatform.AI.UnitTests;

public sealed class GeminiLlmClientTests
{
    [Fact]
    public async Task GenerateStructuredAsync_SendsSchemaAndMapsResponse()
    {
        HttpRequestMessage? captured = null;
        string? requestBody = null;
        var handler = new StubHandler(async request =>
        {
            captured = request;
            requestBody = await request.Content!.ReadAsStringAsync();
            return JsonResponse(HttpStatusCode.OK, """
                {
                  "candidates": [{"content":{"parts":[{"text":"{\"title\":\"Moon\"}"}]}}],
                  "usageMetadata":{"promptTokenCount":12,"candidatesTokenCount":7},
                  "modelVersion":"gemini-2.5-flash"
                }
                """);
        });
        var client = CreateClient(handler);
        using var schemaDocument = JsonDocument.Parse("""{"type":"object","properties":{"title":{"type":"string"}},"required":["title"]}""");

        var result = await client.GenerateStructuredAsync(
            "Create a title", "story_outline", schemaDocument.RootElement);

        Assert.Equal("{\"title\":\"Moon\"}", result.Content);
        Assert.Equal("Gemini", result.ModelProvider);
        Assert.Equal("gemini-2.5-flash", result.Model);
        Assert.Equal("gemini-2.5-flash", result.ModelVersion);
        Assert.Equal(12, result.InputTokens);
        Assert.Equal(7, result.OutputTokens);
        Assert.NotNull(captured);
        Assert.Equal(
            "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent",
            captured.RequestUri!.AbsoluteUri);
        Assert.Equal("test-key", Assert.Single(captured.Headers.GetValues("x-goog-api-key")));
        Assert.Null(captured.Headers.Authorization);
        Assert.Contains("\"responseMimeType\":\"application/json\"", requestBody);
        Assert.Contains("\"responseSchema\"", requestBody);
        Assert.Contains("Create a title", requestBody);
    }

    [Fact]
    public async Task GenerateStructuredAsync_MissingKey_FailsBeforeHttpCall()
    {
        var called = false;
        var handler = new StubHandler(_ =>
        {
            called = true;
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, "{}"));
        });
        var client = CreateClient(handler, apiKey: string.Empty);
        using var schemaDocument = JsonDocument.Parse("""{"type":"object"}""");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.GenerateStructuredAsync("prompt", "schema", schemaDocument.RootElement));

        Assert.Contains("GEMINI_API_KEY", exception.Message);
        Assert.False(called);
    }

    private static GeminiLlmClient CreateClient(HttpMessageHandler handler, string apiKey = "test-key") =>
        new(
            new HttpClient(handler),
            Options.Create(new GeminiOptions
            {
                ApiKey = apiKey,
                Model = "gemini-2.5-flash"
            }));

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json) => new(statusCode)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => responder(request);
    }
}
