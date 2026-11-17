using Microsoft.Extensions.Logging.Abstractions;
using StoryPlatform.Application.Features.MediaGeneration;
using StoryPlatform.Application.Features.MediaGeneration.Interfaces;
using StoryPlatform.Application.Features.MediaGeneration.Models;
using StoryPlatform.Application.Features.MediaGeneration.Services;
using StoryPlatform.Application.Features.MediaStorage.Models;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed class MediaProviderBoundaryTests
{
    [Fact]
    public async Task SemanticSceneSegmentationService_BoundaryException_DoesNotFallBackToParagraph()
    {
        var fallbackCalls = 0;
        var primary = new ThrowingPrimaryProvider(new MediaProviderContextException(MediaProviderContextInvalidReason.MalformedJson));
        var fallback = new CountingParagraphProvider(() => fallbackCalls++);
        var service = new SemanticSceneSegmentationService(
            primary, fallback, NullLogger<SemanticSceneSegmentationService>.Instance);

        var validContext = new ProviderMediaContext(
            2, new ProviderStoryFacts("Title", null, null, "A\n\nB"), ProviderVisualDesign.CreateDefault());

        var ex = await Assert.ThrowsAsync<MediaProviderContextException>(() =>
            service.SegmentAsync(new SceneSegmentationRequest("A\n\nB", [], validContext)));

        Assert.Equal("MEDIA_PROVIDER_CONTEXT_INVALID", ex.ErrorCode);
        Assert.Equal(0, fallbackCalls);
    }

    [Fact]
    public async Task ProcessNext_PersistedInvalidMediaContext_FailsClosedAtSegmentationWithoutFallback()
    {
        var uow = MediaGenerationServiceTests.Seed();
        uow.Seed(new MediaContext
        {
            Id = 50,
            StoryVersionId = 10,
            Revision = 1,
            ContextJson = """{"schemaVersion":1,"storyFacts":{"title":"Old","canonicalContent":"A\n\nB"}}"""
        });

        var finalizer = new MediaGenerationServiceTests.RecordingFinalizer();
        var segmenterCalls = 0;
        var service = MediaGenerationServiceTests.Create(
            uow,
            finalizer: finalizer,
            segmenter: new CountingSegmenter(() => segmenterCalls++));

        var result = await service.ProcessNextAsync();

        Assert.False(result.Success);
        Assert.True(result.IsPermanentFailure);
        Assert.Equal("MEDIA_PROVIDER_CONTEXT_INVALID", result.ErrorCode);
        Assert.Equal("MEDIA_PROVIDER_CONTEXT_INVALID", finalizer.ErrorCode);
        Assert.Equal(0, segmenterCalls);
        Assert.Empty(uow.Items<StoryScene>());
    }

    [Fact]
    public async Task ProcessNext_PersistedContextWithSentinels_ProjectsAtEgressWithoutLeaking()
    {
        var uow = MediaGenerationServiceTests.Seed();
        var internalJson = new MediaContextBuilder().Build(new MediaContextBuildRequest(
            10,
            "Kind Rabbit",
            "Paragraph one.\n\nParagraph two.",
            "Kindness",
            null,
            null,
            null,
            """{"creatorUserId":"SENTINEL_CREATOR_42"}""",
            """{"childProfileId":"SENTINEL_CHILD_42","ttsConfigurationFingerprint":"SENTINEL_TTS_42"}"""));

        uow.Seed(new MediaContext
        {
            Id = 51,
            StoryVersionId = 10,
            Revision = 1,
            ContextJson = internalJson
        });

        var capturedSegmentJson = new List<string>();
        var capturedBeatJson = new List<string>();
        var capturedImageJson = new List<string>();

        var service = MediaGenerationServiceTests.Create(
            uow,
            segmenter: new CapturingSegmenter(ctx => capturedSegmentJson.Add(ProviderMediaContextSerializer.Serialize(ctx))),
            planner: new CapturingBeatPlanner(ctx => capturedBeatJson.Add(ProviderMediaContextSerializer.Serialize(ctx))),
            image: new CapturingImageProvider(ctx => capturedImageJson.Add(ProviderMediaContextSerializer.Serialize(ctx))));

        var result = await service.RunPackageAsync();

        Assert.True(result.Success);
        Assert.NotEmpty(capturedSegmentJson);
        Assert.NotEmpty(capturedBeatJson);
        Assert.NotEmpty(capturedImageJson);

        foreach (var payload in capturedSegmentJson.Concat(capturedBeatJson).Concat(capturedImageJson))
        {
            Assert.Contains("Kind Rabbit", payload, StringComparison.Ordinal);
            Assert.DoesNotContain("SENTINEL_CREATOR_42", payload, StringComparison.Ordinal);
            Assert.DoesNotContain("SENTINEL_CHILD_42", payload, StringComparison.Ordinal);
            Assert.DoesNotContain("SENTINEL_TTS_42", payload, StringComparison.Ordinal);
            Assert.DoesNotContain("acceptedInput", payload, StringComparison.Ordinal);
            Assert.DoesNotContain("acceptedContext", payload, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ProcessNext_BoundaryExceptionDuringBeatPlanning_DoesNotFallBackToWholeSceneBeat()
    {
        var uow = MediaGenerationServiceTests.Seed();
        uow.Seed(new MediaContext
        {
            Id = 52,
            StoryVersionId = 10,
            Revision = 1,
            ContextJson = new MediaContextBuilder().Build(new MediaContextBuildRequest(
                10, "Title", "One\n\nTwo", null, null, null, null, null, null))
        });

        var finalizer = new MediaGenerationServiceTests.RecordingFinalizer();
        var service = MediaGenerationServiceTests.Create(
            uow,
            finalizer: finalizer,
            planner: new ThrowingBeatPlanner(new MediaProviderContextException(MediaProviderContextInvalidReason.OutputContractViolation)));

        var result = await service.ProcessNextAsync();

        Assert.False(result.Success);
        Assert.True(result.IsPermanentFailure);
        Assert.Equal("MEDIA_PROVIDER_CONTEXT_INVALID", result.ErrorCode);
        Assert.Equal("MEDIA_PROVIDER_CONTEXT_INVALID", finalizer.ErrorCode);
        Assert.Empty(uow.Items<IllustrationBeat>());
    }

    [Fact]
    public async Task ProcessBranchNext_CorruptedPersistedContext_FailsImageTaskPermanentlyWithoutRetry()
    {
        var uow = MediaGenerationServiceTests.Seed();
        var validJson = new MediaContextBuilder().Build(new MediaContextBuildRequest(
            10, "Title", "One\n\nTwo", null, null, null, null, null, null));
        var context = new MediaContext { Id = 53, StoryVersionId = 10, Revision = 1, ContextJson = validJson };
        uow.Seed(context);

        var imageCalls = 0;
        var service = MediaGenerationServiceTests.Create(
            uow,
            image: new CapturingImageProvider(_ => imageCalls++));

        // Process coordinator first to queue image tasks
        var coordinator = await service.ProcessNextAsync();
        Assert.True(coordinator.Success);

        // Corrupt persisted MediaContext before image worker executes
        context.ContextJson = "{corrupted";

        var branchResult = await service.ProcessBranchNextAsync(MediaType.Illustration);

        Assert.False(branchResult.Success);
        Assert.True(branchResult.IsPermanentFailure);
        Assert.Equal("MEDIA_PROVIDER_CONTEXT_INVALID", branchResult.ErrorCode);
        Assert.Equal(0, imageCalls);

        var tasks = uow.Items<MediaAssetGenerationTask>();
        Assert.Equal(2, tasks.Count);
        var failedTask = Assert.Single(tasks, x => x.Status == GenerationJobStatus.Failed);
        Assert.Equal("MEDIA_PROVIDER_CONTEXT_INVALID", failedTask.ErrorCode);
        Assert.Null(failedTask.NextAttemptAt);
        var untouchedTask = Assert.Single(tasks, x => x.Id != failedTask.Id);
        Assert.Equal(GenerationJobStatus.Pending, untouchedTask.Status);
        Assert.Equal(0, untouchedTask.AttemptNo);
    }

    private sealed class ThrowingPrimaryProvider(Exception ex) : ISemanticSceneSegmentationProvider
    {
        public Task<IReadOnlyList<SceneSelection>> SegmentAsync(
            SceneSegmentationRequest request, CancellationToken cancellationToken = default) => throw ex;
    }

    private sealed class CountingParagraphProvider(Action onCall) : IParagraphSceneSegmentationProvider
    {
        public Task<IReadOnlyList<SceneSelection>> SegmentAsync(
            SceneSegmentationRequest request, CancellationToken cancellationToken = default)
        {
            onCall();
            return Task.FromResult<IReadOnlyList<SceneSelection>>([]);
        }
    }

    private sealed class CountingSegmenter(Action onCall) : ISceneSegmentationProvider
    {
        public Task<IReadOnlyList<SceneSelection>> SegmentAsync(
            SceneSegmentationRequest request, CancellationToken cancellationToken = default)
        {
            onCall();
            return new ParagraphSceneSegmentationProvider().SegmentAsync(request, cancellationToken);
        }
    }

    private sealed class CapturingSegmenter(Action<ProviderMediaContext> capture) : ISceneSegmentationProvider
    {
        public Task<IReadOnlyList<SceneSelection>> SegmentAsync(
            SceneSegmentationRequest request, CancellationToken cancellationToken = default)
        {
            capture(request.MediaContext);
            return new ParagraphSceneSegmentationProvider().SegmentAsync(request, cancellationToken);
        }
    }

    private sealed class CapturingBeatPlanner(Action<ProviderMediaContext> capture) : IIllustrationBeatPlanner
    {
        public Task<IReadOnlyList<IllustrationBeatSelection>> PlanAsync(
            IllustrationBeatPlanRequest request, CancellationToken cancellationToken = default)
        {
            capture(request.MediaContext);
            return Task.FromResult<IReadOnlyList<IllustrationBeatSelection>>(
                [new IllustrationBeatSelection(1, 0, request.SceneText.Length, "Scene moment")]);
        }
    }

    private sealed class ThrowingBeatPlanner(Exception ex) : IIllustrationBeatPlanner
    {
        public Task<IReadOnlyList<IllustrationBeatSelection>> PlanAsync(
            IllustrationBeatPlanRequest request, CancellationToken cancellationToken = default) => throw ex;
    }

    private sealed class CapturingImageProvider(Action<ProviderMediaContext> capture) : IImageGenerationProvider
    {
        private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x00];

        public Task<GeneratedMedia> GenerateAsync(
            SceneSpecification specification, CancellationToken cancellationToken = default)
        {
            capture(specification.MediaContext);
            return Task.FromResult(new GeneratedMedia(Png, "image/png"));
        }
    }
}
