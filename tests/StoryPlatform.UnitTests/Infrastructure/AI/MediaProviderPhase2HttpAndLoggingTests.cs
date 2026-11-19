using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StoryPlatform.Application.Features.MediaGeneration;
using StoryPlatform.Application.Features.MediaGeneration.Interfaces;
using StoryPlatform.Application.Features.MediaGeneration.Models;
using StoryPlatform.Application.Features.MediaGeneration.Services;
using StoryPlatform.Application.Features.MediaStorage.Models;
using StoryPlatform.Infrastructure.AI;
using StoryPlatform.Infrastructure.Storage;
using Xunit;

namespace StoryPlatform.UnitTests.Infrastructure.AI;

public sealed class MediaProviderPhase2HttpAndLoggingTests
{
    private static readonly byte[] ValidPngBytes =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x00];

    private static readonly byte[] ValidWavBytes =
        [0x52, 0x49, 0x46, 0x46, 0x24, 0x00, 0x00, 0x00, 0x57, 0x41, 0x56, 0x45,
         0x66, 0x6D, 0x74, 0x20, 0x10, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00,
         0x80, 0x3E, 0x00, 0x00, 0x00, 0x7D, 0x00, 0x00, 0x02, 0x00, 0x10, 0x00,
         0x64, 0x61, 0x74, 0x61, 0x00, 0x00, 0x00, 0x00];

    private static ProviderMediaContext BuildSafeProjectedContext(string title = "The Paper Boat", string content = "An folded a paper boat.")
    {
        var internalJson = new MediaContextBuilder().Build(new MediaContextBuildRequest(
            42,
            title,
            content,
            "Kindness",
            "Opening",
            "Development",
            "Ending",
            """{"creatorUserId":"SENTINEL_CREATOR_888","childProfileId":"SENTINEL_CHILD_999"}""",
            """{"audience":{"childProfileId":"SENTINEL_AUDIENCE_555"},"ttsConfigurationFingerprint":"SENTINEL_FP_666"}"""));
        return new ProviderMediaContextProjector().Project(internalJson);
    }

    [Fact]
    public async Task AllSixActiveChannels_RejectPiiBeforeHttpDispatch()
    {
        var httpCalls = 0;
        var handler = new CapturingHttpMessageHandler((_, _) =>
        {
            httpCalls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        var geminiOpts = Options.Create(new GeminiOptions { ApiKey = "test-key" });
        var vertexOpts = Options.Create(new VertexOptions { UseVertex = false });

        var segmenter = new GeminiSemanticSceneSegmentationProvider(
            new HttpClient(handler), geminiOpts, vertexOpts, NullLogger<GeminiSemanticSceneSegmentationProvider>.Instance);
        var planner = new GeminiIllustrationBeatPlanner(
            new HttpClient(handler), geminiOpts, vertexOpts, NullLogger<GeminiIllustrationBeatPlanner>.Instance);
        var imager = new GeminiImageGenerationProvider(
            new HttpClient(handler), geminiOpts, vertexOpts, Options.Create(new ImageGenerationOptions()),
            NullLogger<GeminiImageGenerationProvider>.Instance);
        var alignment = new GeminiMediaAlignmentEvaluator(
            new HttpClient(handler), geminiOpts, vertexOpts, Options.Create(new MediaEvaluationOptions()),
            NullLogger<GeminiMediaAlignmentEvaluator>.Instance);
        var safety = new GeminiMediaSafetyEvaluator(
            new HttpClient(handler), geminiOpts, vertexOpts, Options.Create(new MediaEvaluationOptions()),
            NullLogger<GeminiMediaSafetyEvaluator>.Instance);
        var tts = new GeminiTtsProvider(
            new HttpClient(handler), geminiOpts, vertexOpts, Options.Create(new TtsServiceOptions()),
            NullLogger<GeminiTtsProvider>.Instance);

        var piiContext = BuildSafeProjectedContext(title: "Story for child@example.com");
        var safeContext = BuildSafeProjectedContext();
        var specWithFeedbackPii = new SceneSpecification(1, 2, 0, "Safe scene.", "Safe visual", safeContext, ["Tree"], ["Car"])
            .WithFeedback("Add 0912345678 on the sign");
        var specWithMustShowPii = new SceneSpecification(1, 2, 0, "Safe scene.", "Safe visual", safeContext, ["kid@example.com"], []);
        var media = new GeneratedMedia(ValidPngBytes, "image/png");

        await Assert.ThrowsAsync<MediaProviderPrivacyException>(() =>
            segmenter.SegmentAsync(new SceneSegmentationRequest("Safe", [new StoryTextBlock("P1", 0, 4, "Safe")], piiContext)));
        await Assert.ThrowsAsync<MediaProviderPrivacyException>(() =>
            planner.PlanAsync(new IllustrationBeatPlanRequest("Call +84 912 345 678", "Visual", safeContext)));
        await Assert.ThrowsAsync<MediaProviderPrivacyException>(() =>
            imager.GenerateAsync(specWithFeedbackPii));
        await Assert.ThrowsAsync<MediaProviderPrivacyException>(() =>
            alignment.EvaluateAsync(specWithMustShowPii, media));
        await Assert.ThrowsAsync<MediaProviderPrivacyException>(() =>
            safety.EvaluateAsync(specWithMustShowPii, media));
        await Assert.ThrowsAsync<MediaProviderPrivacyException>(() =>
            tts.GenerateAsync("Xin chào child@example.com"));

        Assert.Equal(0, httpCalls);
    }

    [Theory]
    [InlineData("scene")]
    [InlineData("visual")]
    [InlineData("beat")]
    [InlineData("beat_focus")]
    [InlineData("must_show")]
    [InlineData("must_not_contradict")]
    [InlineData("feedback")]
    [InlineData("context")]
    public async Task Evaluators_RejectPiiAcrossAllSceneSpecificationFields_BeforeHttpDispatch(string fieldGroup)
    {
        var httpCalls = 0;
        var handler = new CapturingHttpMessageHandler((_, _) =>
        {
            httpCalls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        var geminiOpts = Options.Create(new GeminiOptions { ApiKey = "test-key" });
        var vertexOpts = Options.Create(new VertexOptions { UseVertex = false });
        var alignment = new GeminiMediaAlignmentEvaluator(
            new HttpClient(handler), geminiOpts, vertexOpts, Options.Create(new MediaEvaluationOptions()),
            NullLogger<GeminiMediaAlignmentEvaluator>.Instance);
        var safety = new GeminiMediaSafetyEvaluator(
            new HttpClient(handler), geminiOpts, vertexOpts, Options.Create(new MediaEvaluationOptions()),
            NullLogger<GeminiMediaSafetyEvaluator>.Instance);

        var safeContext = BuildSafeProjectedContext();
        var piiContext = BuildSafeProjectedContext(title: "Story for kid@example.com");
        var spec = fieldGroup switch
        {
            "scene" => new SceneSpecification(1, 2, 0, "Email kid@example.com", "Safe visual", safeContext, ["Tree"], ["Car"]),
            "visual" => new SceneSpecification(1, 2, 0, "Safe scene.", "Draw kid@example.com", safeContext, ["Tree"], ["Car"]),
            "beat" => new SceneSpecification(1, 2, 0, "Safe scene.", "Safe visual", safeContext, ["Tree"], ["Car"], null, "Beat kid@example.com", "Focus"),
            "beat_focus" => new SceneSpecification(1, 2, 0, "Safe scene.", "Safe visual", safeContext, ["Tree"], ["Car"], null, "Beat", "Focus kid@example.com"),
            "must_show" => new SceneSpecification(1, 2, 0, "Safe scene.", "Safe visual", safeContext, ["kid@example.com"], ["Car"]),
            "must_not_contradict" => new SceneSpecification(1, 2, 0, "Safe scene.", "Safe visual", safeContext, ["Tree"], ["kid@example.com"]),
            "feedback" => new SceneSpecification(1, 2, 0, "Safe scene.", "Safe visual", safeContext, ["Tree"], ["Car"], "Fix 0912345678"),
            "context" => new SceneSpecification(1, 2, 0, "Safe scene.", "Safe visual", piiContext, ["Tree"], ["Car"]),
            _ => throw new ArgumentOutOfRangeException(nameof(fieldGroup))
        };
        var media = new GeneratedMedia(ValidPngBytes, "image/png");

        await Assert.ThrowsAsync<MediaProviderPrivacyException>(() => alignment.EvaluateAsync(spec, media));
        await Assert.ThrowsAsync<MediaProviderPrivacyException>(() => safety.EvaluateAsync(spec, media));
        Assert.Equal(0, httpCalls);
    }

    [Fact]
    public async Task Evaluators_SafeSpecification_DoesNotInjectFeedbackOrContextIntoOutboundPrompt()
    {
        string? alignmentBody = null;
        string? safetyBody = null;
        var evalResponse = JsonSerializer.Serialize(new
        {
            candidates = new[]
            {
                new { content = new { parts = new[] { new { text = """{"alignmentScore":0.95,"containsVisibleText":false,"isSafe":true,"reason":"ok"}""" } } } }
            }
        });
        var geminiOpts = Options.Create(new GeminiOptions { ApiKey = "test-key" });
        var vertexOpts = Options.Create(new VertexOptions { UseVertex = false });
        var alignment = new GeminiMediaAlignmentEvaluator(
            new HttpClient(new CapturingHttpMessageHandler(async (req, ct) =>
            {
                alignmentBody = await req.Content!.ReadAsStringAsync(ct);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(evalResponse, Encoding.UTF8, "application/json") };
            })), geminiOpts, vertexOpts, Options.Create(new MediaEvaluationOptions()), NullLogger<GeminiMediaAlignmentEvaluator>.Instance);
        var safety = new GeminiMediaSafetyEvaluator(
            new HttpClient(new CapturingHttpMessageHandler(async (req, ct) =>
            {
                safetyBody = await req.Content!.ReadAsStringAsync(ct);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(evalResponse, Encoding.UTF8, "application/json") };
            })), geminiOpts, vertexOpts, Options.Create(new MediaEvaluationOptions()), NullLogger<GeminiMediaSafetyEvaluator>.Instance);

        var safeContext = BuildSafeProjectedContext(title: "UNIQUE_CONTEXT_TITLE_MARKER");
        var spec = new SceneSpecification(1, 2, 0, "Safe scene.", "Safe visual", safeContext, ["UNIQUE_MUST_SHOW"], ["UNIQUE_MUST_NOT"], "UNIQUE_FEEDBACK_MARKER");
        var media = new GeneratedMedia(ValidPngBytes, "image/png");

        await alignment.EvaluateAsync(spec, media);
        await safety.EvaluateAsync(spec, media);

        Assert.NotNull(alignmentBody);
        Assert.DoesNotContain("UNIQUE_CONTEXT_TITLE_MARKER", alignmentBody);
        Assert.DoesNotContain("UNIQUE_FEEDBACK_MARKER", alignmentBody);
        Assert.NotNull(safetyBody);
        Assert.DoesNotContain("UNIQUE_CONTEXT_TITLE_MARKER", safetyBody);
        Assert.DoesNotContain("UNIQUE_FEEDBACK_MARKER", safetyBody);
        Assert.DoesNotContain("UNIQUE_MUST_SHOW", safetyBody);
        Assert.DoesNotContain("UNIQUE_MUST_NOT", safetyBody);
    }

    [Fact]
    public async Task EvaluatorsAndTts_ParseActualHttpJsonPayloads_AndNeverLogBinaryOrPrompt()
    {
        string? alignmentJson = null;
        string? safetyJson = null;
        string? ttsJson = null;

        var evalResponse = JsonSerializer.Serialize(new
        {
            candidates = new[]
            {
                new { content = new { parts = new[] { new { text = """{"alignmentScore":0.95,"containsVisibleText":false,"isSafe":true,"reason":"ok"}""" } } } }
            },
            usageMetadata = new { promptTokenCount = 12, candidatesTokenCount = 5, totalTokenCount = 17 }
        });
        var ttsResponse = JsonSerializer.Serialize(new
        {
            candidates = new[]
            {
                new { content = new { parts = new[] { new { inlineData = new { mimeType = "audio/wav", data = Convert.ToBase64String(ValidWavBytes) } } } } }
            }
        });

        var alignLogger = new CaptureLogger<GeminiMediaAlignmentEvaluator>();
        var safetyLogger = new CaptureLogger<GeminiMediaSafetyEvaluator>();
        var ttsLogger = new CaptureLogger<GeminiTtsProvider>();

        var alignProvider = new GeminiMediaAlignmentEvaluator(
            new HttpClient(new CapturingHttpMessageHandler(async (req, ct) =>
            {
                alignmentJson = await req.Content!.ReadAsStringAsync(ct);
                var resp = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(evalResponse, Encoding.UTF8, "application/json") };
                resp.Headers.Add("x-goog-request-id", "req-align-123");
                return resp;
            })),
            Options.Create(new GeminiOptions { ApiKey = "test-key" }),
            Options.Create(new VertexOptions { UseVertex = false }),
            Options.Create(new MediaEvaluationOptions()),
            alignLogger);

        var safetyProvider = new GeminiMediaSafetyEvaluator(
            new HttpClient(new CapturingHttpMessageHandler(async (req, ct) =>
            {
                safetyJson = await req.Content!.ReadAsStringAsync(ct);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(evalResponse, Encoding.UTF8, "application/json") };
            })),
            Options.Create(new GeminiOptions { ApiKey = "test-key" }),
            Options.Create(new VertexOptions { UseVertex = false }),
            Options.Create(new MediaEvaluationOptions()),
            safetyLogger);

        var ttsProvider = new GeminiTtsProvider(
            new HttpClient(new CapturingHttpMessageHandler(async (req, ct) =>
            {
                ttsJson = await req.Content!.ReadAsStringAsync(ct);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ttsResponse, Encoding.UTF8, "application/json") };
            })),
            Options.Create(new GeminiOptions { ApiKey = "test-key" }),
            Options.Create(new VertexOptions { UseVertex = false }),
            Options.Create(new TtsServiceOptions()),
            ttsLogger);

        var spec = new SceneSpecificationBuilder().Build(42, 10, 0, "Bé An gấp thuyền giấy.", "Thuyền giấy", BuildSafeProjectedContext());
        var image = new GeneratedMedia(ValidPngBytes, "image/png");

        var alignResult = await alignProvider.EvaluateAsync(spec, image);
        var safetyResult = await safetyProvider.EvaluateAsync(spec, image);
        var audioResult = await ttsProvider.GenerateAsync("Bé An gấp thuyền giấy.");

        Assert.True(alignResult.Passed);
        Assert.True(safetyResult.Passed);
        Assert.Equal("audio/wav", audioResult.MimeType);

        using var alignDoc = JsonDocument.Parse(alignmentJson!);
        var alignParts = alignDoc.RootElement.GetProperty("contents")[0].GetProperty("parts");
        Assert.Contains("Bé An gấp thuyền giấy.", alignParts[0].GetProperty("text").GetString());
        Assert.Equal(Convert.ToBase64String(ValidPngBytes), alignParts[1].GetProperty("inlineData").GetProperty("data").GetString());

        using var safetyDoc = JsonDocument.Parse(safetyJson!);
        Assert.Equal("image/png", safetyDoc.RootElement.GetProperty("contents")[0].GetProperty("parts")[1].GetProperty("inlineData").GetProperty("mimeType").GetString());

        using var ttsDoc = JsonDocument.Parse(ttsJson!);
        Assert.Equal("Bé An gấp thuyền giấy.", ttsDoc.RootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString());

        var alignLogText = string.Join('\n', alignLogger.FormattedMessages);
        Assert.Contains("req-align-123", alignLogText);
        Assert.Contains("PromptTokens=12", alignLogText);
        Assert.DoesNotContain(Convert.ToBase64String(ValidPngBytes), alignLogText);
        Assert.DoesNotContain("Bé An gấp thuyền giấy.", alignLogText);

        var ttsLogText = string.Join('\n', ttsLogger.FormattedMessages);
        Assert.Contains("PromptTokens=", ttsLogText);
        Assert.DoesNotContain(Convert.ToBase64String(ValidWavBytes), ttsLogText);
        Assert.DoesNotContain("Bé An gấp thuyền giấy.", ttsLogText);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task ImageProvider_HttpError_NeverLeaksErrorBodySignedUrlOrCrlfIntoLogsOrExceptions(HttpStatusCode status)
    {
        const string sensitiveBody =
            "{\"error\":\"leak kid@example.com AIzaSyDummyKey1234567890123456 https://s.example.com/obj?sig=secret\r\nFORGED_LINE\"}";
        var logger = new CaptureLogger<GeminiImageGenerationProvider>();

        var provider = new GeminiImageGenerationProvider(
            new HttpClient(new CapturingHttpMessageHandler((_, _) =>
            {
                var resp = new HttpResponseMessage(status)
                {
                    Content = new StringContent(sensitiveBody, Encoding.UTF8, "application/json")
                };
                resp.Headers.TryAddWithoutValidation("Retry-After", "15");
                return Task.FromResult(resp);
            })),
            Options.Create(new GeminiOptions { ApiKey = "test-key" }),
            Options.Create(new VertexOptions { UseVertex = false }),
            Options.Create(new ImageGenerationOptions()),
            logger);

        var spec = new SceneSpecificationBuilder().Build(42, 10, 0, "An folded a boat.", "Paper boat", BuildSafeProjectedContext());

        Exception caught = status is HttpStatusCode.TooManyRequests or HttpStatusCode.InternalServerError
            ? await Assert.ThrowsAsync<TransientMediaGenerationException>(() => provider.GenerateAsync(spec))
            : await Assert.ThrowsAsync<PermanentMediaGenerationException>(() => provider.GenerateAsync(spec));

        Assert.DoesNotContain("kid@example.com", caught.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("AIzaSyDummyKey", caught.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("sig=secret", caught.Message, StringComparison.Ordinal);

        var logText = string.Join('\n', logger.FormattedMessages);
        Assert.Contains($"HttpStatus={(int)status}", logText, StringComparison.Ordinal);
        Assert.Contains("RetryAfterSeconds=15", logText, StringComparison.Ordinal);
        Assert.DoesNotContain("kid@example.com", logText, StringComparison.Ordinal);
        Assert.DoesNotContain("AIzaSyDummyKey", logText, StringComparison.Ordinal);
        Assert.DoesNotContain("sig=secret", logText, StringComparison.Ordinal);
        Assert.DoesNotContain("FORGED_LINE", logText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SupabaseMediaStorage_ErrorResponse_NeverExposesSignedUrlCredentialOrBinary()
    {
        const string sensitiveError =
            "{\"error\":\"https://proj.supabase.co/storage/v1/object/sign/b/k?token=sb_secret_1234567890abcdef\"}";
        var logger = new CaptureLogger<SupabaseMediaStorage>();
        var storage = new SupabaseMediaStorage(
            new HttpClient(new CapturingHttpMessageHandler((_, _) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway)
                {
                    Content = new StringContent(sensitiveError, Encoding.UTF8, "application/json")
                }))),
            new SupabaseStorageOptions { Url = "https://proj.supabase.co", Bucket = "story-media", SecretKey = "sb_secret_key" },
            logger);

        using var stream = new MemoryStream(ValidPngBytes);
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            storage.UploadAsync("stories/1/img.png", stream, "image/png"));

        Assert.DoesNotContain("sb_secret_", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("token=", ex.Message, StringComparison.Ordinal);
        var logText = string.Join('\n', logger.FormattedMessages);
        Assert.Contains("Status=502", logText, StringComparison.Ordinal);
        Assert.DoesNotContain("sb_secret_", logText, StringComparison.Ordinal);
        Assert.DoesNotContain("token=", logText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrivacyException_PropagatesWithoutFallbackOrCircuitBreakerTrip()
    {
        var fallbackCalled = false;
        var segmentation = new SemanticSceneSegmentationService(
            new ThrowingPrivacySegmentationProvider(),
            new StubParagraphFallback(() => fallbackCalled = true),
            NullLogger<SemanticSceneSegmentationService>.Instance);

        var context = BuildSafeProjectedContext();
        await Assert.ThrowsAsync<MediaProviderPrivacyException>(() =>
            segmentation.SegmentAsync(new SceneSegmentationRequest("Safe", [new StoryTextBlock("P1", 0, 4, "Safe")], context)));
        Assert.False(fallbackCalled);

        var gate = new MediaBranchProviderGate(new MediaBranchOptions
        {
            CircuitBreaker = new CircuitBreakerConfig { FailureThreshold = 2, DurationOfBreakSeconds = 60 }
        });
        var gatedTts = new GatedTtsProvider(new ThrowingPrivacyTtsProvider(), gate);

        for (var i = 0; i < 4; i++)
        {
            await Assert.ThrowsAsync<MediaProviderPrivacyException>(() => gatedTts.GenerateAsync("safe"));
        }
    }

    private sealed class ThrowingPrivacySegmentationProvider : ISemanticSceneSegmentationProvider
    {
        public Task<IReadOnlyList<SceneSelection>> SegmentAsync(SceneSegmentationRequest request, CancellationToken cancellationToken = default) =>
            throw new MediaProviderPrivacyException(MediaProviderPrivacyReason.EmailDetected);
    }

    private sealed class StubParagraphFallback(Action onCall) : IParagraphSceneSegmentationProvider
    {
        public Task<IReadOnlyList<SceneSelection>> SegmentAsync(SceneSegmentationRequest request, CancellationToken cancellationToken = default)
        {
            onCall();
            return Task.FromResult<IReadOnlyList<SceneSelection>>([]);
        }
    }

    private sealed class ThrowingPrivacyTtsProvider : ITtsProvider
    {
        public string? ConfigurationFingerprint => "fp";
        public Task<GeneratedMedia> GenerateAsync(string exactSceneText, CancellationToken cancellationToken = default) =>
            throw new MediaProviderPrivacyException(MediaProviderPrivacyReason.PhoneNumberDetected);
    }

    private sealed class CapturingHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            handler(request, cancellationToken);
    }

    private sealed class CaptureLogger<T> : ILogger<T>
    {
        public ConcurrentQueue<string> FormattedMessages { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => FormattedMessages.Enqueue(formatter(state, exception));
    }
}
