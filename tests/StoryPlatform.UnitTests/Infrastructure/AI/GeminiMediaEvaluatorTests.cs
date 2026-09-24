using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StoryPlatform.Application.Features.MediaGeneration.Models;
using StoryPlatform.Application.Features.MediaStorage.Models;
using StoryPlatform.Infrastructure.AI;
using Xunit;

namespace StoryPlatform.UnitTests.Infrastructure.AI;

public sealed class GeminiMediaEvaluatorTests
{
    private static readonly SceneSpecification Specification = new(
        1, 1, 0, "Lan giúp một chú chim nhỏ.", "Lan đứng cạnh chú chim trong khu vườn.", "{}",
        ["Lan", "chú chim"], ["bạo lực"], null);
    private static readonly GeneratedMedia Illustration = new(
        [0x89, 0x50, 0x4E, 0x47], "image/png");

    [Fact]
    public async Task AlignmentEvaluator_sends_image_and_parses_vertex_text_envelope()
    {
        string? requestBody = null;
        var handler = new MockHandler(async (request, cancellationToken) =>
        {
            requestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return JsonResponse("{\"alignmentScore\":0.92,\"reason\":\"Matches the scene\"}");
        });
        var evaluator = new GeminiMediaAlignmentEvaluator(
            new HttpClient(handler), Options.Create(new GeminiOptions()), Options.Create(Vertex()),
            Options.Create(new MediaEvaluationOptions()), NullLogger<GeminiMediaAlignmentEvaluator>.Instance);

        var result = await evaluator.EvaluateAsync(Specification, Illustration);

        Assert.True(result.Passed);
        using var body = JsonDocument.Parse(requestBody!);
        var parts = body.RootElement.GetProperty("contents")[0].GetProperty("parts");
        Assert.Equal("image/png", parts[1].GetProperty("inlineData").GetProperty("mimeType").GetString());
        Assert.Equal(Convert.ToBase64String(Illustration.Content),
            parts[1].GetProperty("inlineData").GetProperty("data").GetString());
    }

    [Fact]
    public async Task SafetyEvaluator_parses_fenced_json_from_vertex_text_envelope()
    {
        var handler = new MockHandler((request, cancellationToken) =>
            Task.FromResult(JsonResponse("```json\n{\"isSafe\":true,\"concerns\":[]}\n```")));
        var evaluator = new GeminiMediaSafetyEvaluator(
            new HttpClient(handler), Options.Create(new GeminiOptions()), Options.Create(Vertex()),
            Options.Create(new MediaEvaluationOptions()), NullLogger<GeminiMediaSafetyEvaluator>.Instance);

        var result = await evaluator.EvaluateAsync(Specification, Illustration);

        Assert.True(result.Passed);
    }

    private static HttpResponseMessage JsonResponse(string modelText)
    {
        var envelope = JsonSerializer.Serialize(new
        {
            candidates = new[] { new { content = new { parts = new[] { new { text = modelText } } } } }
        });
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(envelope, Encoding.UTF8, "application/json")
        };
    }

    private static VertexOptions Vertex() => new()
    {
        UseVertex = true,
        ProjectId = "test-project",
        Location = "global"
    };

    private sealed class MockHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> callback) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) => callback(request, cancellationToken);
    }
}
