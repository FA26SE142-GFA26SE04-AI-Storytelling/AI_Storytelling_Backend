using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StoryPlatform.Application.Features.MediaGeneration;
using StoryPlatform.Application.Features.MediaGeneration.Models;
using StoryPlatform.Application.Features.MediaGeneration.Services;
using StoryPlatform.Infrastructure.AI;
using Xunit;

namespace StoryPlatform.UnitTests.Infrastructure.AI;

public sealed class MediaProviderBoundaryHttpTests
{
    private static readonly byte[] ValidPngBytes =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x00];

    private static readonly string[] SentinelValues =
    [
        "SENTINEL_CHILD_PROFILE_999",
        "SENTINEL_CREATOR_USER_888",
        "SENTINEL_CONSENT_GUARDIAN_777",
        "SENTINEL_TTS_FINGERPRINT_666",
        "SENTINEL_PACKAGE_AUDIENCE_555",
        "acceptedInput",
        "acceptedContext",
        "childProfileId",
        "creatorUserId",
        "ttsConfigurationFingerprint",
        "sourceStoryVersionId"
    ];

    private static ProviderMediaContext BuildProjectedContextWithSentinels()
    {
        var internalJson = new MediaContextBuilder().Build(new MediaContextBuildRequest(
            99,
            "The Paper Boat",
            "An folded a small paper boat.\n\nThe boat sailed down the stream.",
            "Courage",
            "An makes a boat",
            "The boat sails",
            "An smiles",
            """{"creatorUserId":"SENTINEL_CREATOR_USER_888","childProfileId":"SENTINEL_CHILD_PROFILE_999"}""",
            """{"audience":{"childProfileId":"SENTINEL_PACKAGE_AUDIENCE_555","consentRecordedBy":"SENTINEL_CONSENT_GUARDIAN_777"},"ttsConfigurationFingerprint":"SENTINEL_TTS_FINGERPRINT_666"}"""));

        return new ProviderMediaContextProjector().Project(internalJson);
    }

    [Fact]
    public async Task GeminiSemanticSceneSegmentationProvider_HttpRequest_NeverIncludesInternalMetadataSentinels()
    {
        string? capturedBody = null;
        var responseJson = JsonSerializer.Serialize(new
        {
            candidates = new[]
            {
                new
                {
                    content = new
                    {
                        parts = new[]
                        {
                            new { text = """[{"sceneIndex":0,"blockIds":["P1"],"focus":"Paper boat"}]""" }
                        }
                    }
                }
            }
        });

        var handler = new CapturingHttpMessageHandler(async (req, ct) =>
        {
            capturedBody = await req.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
        });

        var provider = new GeminiSemanticSceneSegmentationProvider(
            new HttpClient(handler),
            Options.Create(new GeminiOptions { ApiKey = "test-key" }),
            Options.Create(new VertexOptions { UseVertex = false }),
            NullLogger<GeminiSemanticSceneSegmentationProvider>.Instance);

        var projected = BuildProjectedContextWithSentinels();
        var blocks = new[] { new StoryTextBlock("P1", 0, 30, "An folded a small paper boat.") };
        var result = await provider.SegmentAsync(new SceneSegmentationRequest("An folded a small paper boat.", blocks, projected));

        Assert.Single(result);
        Assert.NotNull(capturedBody);
        Assert.Contains("The Paper Boat", capturedBody, StringComparison.Ordinal);
        foreach (var sentinel in SentinelValues)
            Assert.DoesNotContain(sentinel, capturedBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GeminiIllustrationBeatPlanner_HttpRequest_NeverIncludesInternalMetadataSentinels()
    {
        string? capturedBody = null;
        const string sceneText = "An folded a small paper boat.";
        var responseJson = JsonSerializer.Serialize(new
        {
            candidates = new[]
            {
                new
                {
                    content = new
                    {
                        parts = new[]
                        {
                            new { text = """[{"beatOrder":1,"anchorText":"An folded a small paper boat.","visualFocus":"An folding paper boat"}]""" }
                        }
                    }
                }
            }
        });

        var handler = new CapturingHttpMessageHandler(async (req, ct) =>
        {
            capturedBody = await req.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
        });

        var planner = new GeminiIllustrationBeatPlanner(
            new HttpClient(handler),
            Options.Create(new GeminiOptions { ApiKey = "test-key" }),
            Options.Create(new VertexOptions { UseVertex = false }),
            NullLogger<GeminiIllustrationBeatPlanner>.Instance);

        var projected = BuildProjectedContextWithSentinels();
        var result = await planner.PlanAsync(new IllustrationBeatPlanRequest(sceneText, "A paper boat", projected));

        Assert.Single(result);
        Assert.NotNull(capturedBody);
        Assert.Contains("The Paper Boat", capturedBody, StringComparison.Ordinal);
        foreach (var sentinel in SentinelValues)
            Assert.DoesNotContain(sentinel, capturedBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GeminiImageGenerationProvider_HttpRequest_NeverIncludesInternalMetadataSentinels()
    {
        string? capturedBody = null;
        var responseJson = JsonSerializer.Serialize(new
        {
            candidates = new[]
            {
                new
                {
                    content = new
                    {
                        parts = new[]
                        {
                            new
                            {
                                inlineData = new
                                {
                                    mimeType = "image/png",
                                    data = Convert.ToBase64String(ValidPngBytes)
                                }
                            }
                        }
                    }
                }
            }
        });

        var handler = new CapturingHttpMessageHandler(async (req, ct) =>
        {
            capturedBody = await req.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
        });

        var provider = new GeminiImageGenerationProvider(
            new HttpClient(handler),
            Options.Create(new GeminiOptions { ApiKey = "test-key" }),
            Options.Create(new VertexOptions { UseVertex = false }),
            Options.Create(new ImageGenerationOptions()),
            NullLogger<GeminiImageGenerationProvider>.Instance);

        var projected = BuildProjectedContextWithSentinels();
        var spec = new SceneSpecificationBuilder().Build(
            99, 10, 0, "An folded a small paper boat.", "A paper boat", projected);

        var media = await provider.GenerateAsync(spec);

        Assert.Equal("image/png", media.MimeType);
        Assert.NotNull(capturedBody);
        Assert.Contains("The Paper Boat", capturedBody, StringComparison.Ordinal);
        foreach (var sentinel in SentinelValues)
            Assert.DoesNotContain(sentinel, capturedBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AllThreeAdapters_InvalidProjectedDto_FailClosedBeforeHttpCall()
    {
        var httpCalls = 0;
        var handler = new CapturingHttpMessageHandler((_, _) =>
        {
            httpCalls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        var invalidDto = new ProviderMediaContext(
            1,
            new ProviderStoryFacts("", null, null, ""),
            ProviderVisualDesign.CreateDefault());

        var segmenter = new GeminiSemanticSceneSegmentationProvider(
            new HttpClient(handler),
            Options.Create(new GeminiOptions { ApiKey = "test-key" }),
            Options.Create(new VertexOptions { UseVertex = false }),
            NullLogger<GeminiSemanticSceneSegmentationProvider>.Instance);

        var planner = new GeminiIllustrationBeatPlanner(
            new HttpClient(handler),
            Options.Create(new GeminiOptions { ApiKey = "test-key" }),
            Options.Create(new VertexOptions { UseVertex = false }),
            NullLogger<GeminiIllustrationBeatPlanner>.Instance);

        var imager = new GeminiImageGenerationProvider(
            new HttpClient(handler),
            Options.Create(new GeminiOptions { ApiKey = "test-key" }),
            Options.Create(new VertexOptions { UseVertex = false }),
            Options.Create(new ImageGenerationOptions()),
            NullLogger<GeminiImageGenerationProvider>.Instance);

        await Assert.ThrowsAsync<MediaProviderContextException>(() =>
            segmenter.SegmentAsync(new SceneSegmentationRequest("Text", [], invalidDto)));
        await Assert.ThrowsAsync<MediaProviderContextException>(() =>
            planner.PlanAsync(new IllustrationBeatPlanRequest("Text", null, invalidDto)));
        await Assert.ThrowsAsync<MediaProviderContextException>(() =>
            imager.GenerateAsync(new SceneSpecification(1, 1, 0, "Text", null, invalidDto, [], [])));

        Assert.Equal(0, httpCalls);
    }

    private sealed class CapturingHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            handler(request, cancellationToken);
    }
}
