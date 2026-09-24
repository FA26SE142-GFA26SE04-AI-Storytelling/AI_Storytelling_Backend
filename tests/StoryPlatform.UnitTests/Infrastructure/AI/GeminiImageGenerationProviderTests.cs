using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StoryPlatform.Application.Features.MediaGeneration;
using StoryPlatform.Application.Features.MediaGeneration.Models;
using StoryPlatform.Infrastructure.AI;
using Xunit;

namespace StoryPlatform.UnitTests.Infrastructure.AI;

public sealed class GeminiImageGenerationProviderTests
{
    private static readonly byte[] ValidPngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x00];

    private static SceneSpecification CreateTestSpec() => new(
        StoryVersionId: 1,
        StorySceneId: 10,
        SceneIndex: 0,
        SceneText: "Bé thỏ trắng dạo chơi trong khu rừng xanh.",
        VisualDescription: "Chú thỏ trắng dễ thương đang nhảy nhót dưới ánh nắng xuyên qua tán cây.",
        MediaContextJson: "{}",
        MustShow: ["Chú thỏ trắng", "Khu rừng"],
        MustNotContradict: ["Trời không có tuyết"],
        PromptFeedback: null);

    private static string CreateGeminiImageResponseJson(byte[] imageBytes, string mimeType = "image/png")
    {
        var base64 = Convert.ToBase64String(imageBytes);
        return $$"""
        {
          "candidates": [
            {
              "content": {
                "parts": [
                  {
                    "inlineData": {
                      "mimeType": "{{mimeType}}",
                      "data": "{{base64}}"
                    }
                  }
                ]
              }
            }
          ]
        }
        """;
    }

    private static GeminiImageGenerationProvider CreateProvider(
        HttpMessageHandler handler,
        ImageGenerationOptions? imageOptions = null,
        VertexOptions? vertexOptions = null,
        GeminiOptions? geminiOptions = null,
        bool useResiliencePipeline = false)
    {
        var resolvedImageOptions = imageOptions ?? new ImageGenerationOptions();
        HttpClient client;
        if (useResiliencePipeline)
        {
            var services = new ServiceCollection();
            var builder = services.AddHttpClient("media-provider-test")
                .ConfigurePrimaryHttpMessageHandler(() => handler);
            builder.AddMediaResiliencePipeline(
                new MediaGenerationOptions(),
                resolvedImageOptions.TransportRetryCount,
                resolvedImageOptions.TransportRetryBaseDelayMs);
            client = services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>()
                .CreateClient("media-provider-test");
        }
        else
        {
            client = new HttpClient(handler);
        }
        var imgOpts = Options.Create(resolvedImageOptions);
        var vtxOpts = Options.Create(vertexOptions ?? new VertexOptions { UseVertex = true, ProjectId = "test-proj", Location = "global" });
        var gemOpts = Options.Create(geminiOptions ?? new GeminiOptions());
        return new GeminiImageGenerationProvider(client, gemOpts, vtxOpts, imgOpts, NullLogger<GeminiImageGenerationProvider>.Instance);
    }

    [Fact]
    public async Task GenerateAsync_ValidSpecification_ReturnsGeneratedMediaWithCorrectMime()
    {
        var json = CreateGeminiImageResponseJson(ValidPngBytes, "image/png");
        var handler = new MockHttpMessageHandler((req, ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            }));

        var provider = CreateProvider(handler);
        var result = await provider.GenerateAsync(CreateTestSpec());

        Assert.NotNull(result);
        Assert.Equal("image/png", result.MimeType);
        Assert.Equal(ValidPngBytes, result.Content);
        Assert.Equal("gemini-3.1-flash-image", result.GetMetadata("model"));
        Assert.Equal("16:9", result.GetMetadata("aspectRatio"));
        Assert.Equal("VertexAI", result.GetMetadata("provider"));
    }

    [Fact]
    public async Task GenerateAsync_SerializesGeminiImageAndSafetyConfig()
    {
        string? capturedBody = null;
        var json = CreateGeminiImageResponseJson(ValidPngBytes);
        var handler = new MockHttpMessageHandler(async (req, ct) =>
        {
            capturedBody = await req.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        });

        var provider = CreateProvider(handler, new ImageGenerationOptions { AspectRatio = "16:9" });
        await provider.GenerateAsync(CreateTestSpec());

        Assert.NotNull(capturedBody);
        using var doc = JsonDocument.Parse(capturedBody);
        var generationConfig = doc.RootElement.GetProperty("generationConfig");
        var imageConfig = generationConfig.GetProperty("imageConfig");
        Assert.Equal("16:9", imageConfig.GetProperty("aspectRatio").GetString());
        Assert.False(imageConfig.TryGetProperty("safetySetting", out _));
        Assert.False(imageConfig.TryGetProperty("personGeneration", out _));
        Assert.Equal(["TEXT", "IMAGE"], generationConfig.GetProperty("responseModalities")
            .EnumerateArray().Select(item => item.GetString()!).ToArray());
        Assert.Equal(1, generationConfig.GetProperty("candidateCount").GetInt32());
        var safetySettings = doc.RootElement.GetProperty("safetySettings");
        Assert.Equal(4, safetySettings.GetArrayLength());
        Assert.All(safetySettings.EnumerateArray(), setting =>
            Assert.Equal("BLOCK_MEDIUM_AND_ABOVE", setting.GetProperty("threshold").GetString()));
        Assert.Contains("People policy: allow_adult", capturedBody);
    }

    [Fact]
    public async Task GenerateAsync_CandidateWithInlineData_ExtractsBase64Successfully()
    {
        var expectedBytes = ValidPngBytes;
        var json = CreateGeminiImageResponseJson(expectedBytes);
        var handler = new MockHttpMessageHandler((req, ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            }));

        var provider = CreateProvider(handler);
        var result = await provider.GenerateAsync(CreateTestSpec());

        Assert.Equal(expectedBytes, result.Content);
    }

    [Fact]
    public async Task GenerateAsync_NoCandidateOrEmptyData_ThrowsPermanentException()
    {
        const string emptyCandidatesJson = @"{ ""candidates"": [] }";
        var handler = new MockHttpMessageHandler((req, ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(emptyCandidatesJson, Encoding.UTF8, "application/json")
            }));

        var provider = CreateProvider(handler);
        var ex = await Assert.ThrowsAsync<PermanentMediaGenerationException>(() =>
            provider.GenerateAsync(CreateTestSpec()));

        Assert.Contains("GEMINI_IMAGE_NO_CANDIDATES", ex.Message);
    }

    [Fact]
    public async Task GenerateAsync_Http400_ThrowsPermanentWithoutRetry()
    {
        var callCount = 0;
        var handler = new MockHttpMessageHandler((req, ct) =>
        {
            callCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("{\"error\":\"Invalid prompt\"}")
            });
        });

        var provider = CreateProvider(handler);
        var ex = await Assert.ThrowsAsync<PermanentMediaGenerationException>(() =>
            provider.GenerateAsync(CreateTestSpec()));

        Assert.Equal(1, callCount); // Must fail fast without blind retries
        Assert.Contains("GEMINI_IMAGE_HTTP_400", ex.Message);
    }

    [Fact]
    public async Task GenerateAsync_Http429Or503_TriggersExponentialBackoffRetry()
    {
        var callCount = 0;
        var successJson = CreateGeminiImageResponseJson(ValidPngBytes);
        var handler = new MockHttpMessageHandler((req, ct) =>
        {
            callCount++;
            if (callCount < 3)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {
                    Content = new StringContent("{\"error\":\"Model overloaded\"}")
                });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(successJson, Encoding.UTF8, "application/json")
            });
        });

        var provider = CreateProvider(handler, new ImageGenerationOptions
        {
            TransportRetryCount = 3,
            TransportRetryBaseDelayMs = 10 // Short delay for fast test
        }, useResiliencePipeline: true);

        var result = await provider.GenerateAsync(CreateTestSpec());

        Assert.NotNull(result);
        Assert.Equal(3, callCount); // Retried twice, succeeded on 3rd attempt
    }

    [Fact]
    public async Task GenerateAsync_ExhaustedHttp429_ReturnsTypedTransientFailure()
    {
        var callCount = 0;
        var handler = new MockHttpMessageHandler((req, ct) =>
        {
            callCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("{\"error\":\"Resource exhausted\"}")
            });
        });
        var provider = CreateProvider(handler, new ImageGenerationOptions
        {
            TransportRetryCount = 2,
            TransportRetryBaseDelayMs = 1
        }, useResiliencePipeline: true);

        var exception = await Assert.ThrowsAsync<TransientMediaGenerationException>(() =>
            provider.GenerateAsync(CreateTestSpec()));

        Assert.Equal(3, callCount);
        Assert.Equal("GEMINI_IMAGE_HTTP_429", exception.ErrorCode);
        var transportError = Assert.IsType<HttpRequestException>(exception.InnerException);
        Assert.Equal(HttpStatusCode.TooManyRequests, transportError.StatusCode);
    }

    [Fact]
    public async Task GenerateAsync_CorruptedBytes_FailsMagicByteValidation()
    {
        var corruptedBytes = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B };
        var json = CreateGeminiImageResponseJson(corruptedBytes);
        var handler = new MockHttpMessageHandler((req, ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            }));

        var provider = CreateProvider(handler);
        var ex = await Assert.ThrowsAsync<PermanentMediaGenerationException>(() =>
            provider.GenerateAsync(CreateTestSpec()));

        Assert.Contains("GEMINI_IMAGE_VALIDATION_FAILED", ex.Message);
    }

    [Fact]
    public async Task GenerateAsync_CallerCancellationToken_PropagatesImmediatelyWithoutRetry()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Pre-canceled token

        var callCount = 0;
        var handler = new MockHttpMessageHandler((req, ct) =>
        {
            callCount++;
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        var provider = CreateProvider(handler);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            provider.GenerateAsync(CreateTestSpec(), cts.Token));

        Assert.Equal(1, callCount); // Must propagate immediately after 1 attempt without retries
    }

    [Fact]
    public async Task GenerateAsync_CancellationDuringBackoff_StopsBeforeNextAttempt()
    {
        var firstAttempt = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource();
        var callCount = 0;
        var handler = new MockHttpMessageHandler((req, ct) =>
        {
            callCount++;
            firstAttempt.TrySetResult();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        });
        var provider = CreateProvider(handler, new ImageGenerationOptions
        {
            TransportRetryCount = 3,
            TransportRetryBaseDelayMs = 5_000
        }, useResiliencePipeline: true);

        var operation = provider.GenerateAsync(CreateTestSpec(), cts.Token);
        await firstAttempt.Task;
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        Assert.Equal(1, callCount);
    }

    private sealed class MockHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsyncFunc)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            sendAsyncFunc(request, cancellationToken);
    }
}
