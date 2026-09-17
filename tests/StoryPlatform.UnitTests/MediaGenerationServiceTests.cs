using StoryPlatform.Application.Features.MediaGeneration;
using StoryPlatform.Application.Features.MediaGeneration.Interfaces;
using StoryPlatform.Application.Features.MediaGeneration.Models;
using StoryPlatform.Application.Features.MediaGeneration.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.Infrastructure.AI;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed class MediaGenerationServiceTests
{
    [Fact]
    public async Task ProcessNext_CreatesExactScenesAndTwoAssetsPerSceneThenMarksReady()
    {
        var uow = Seed();
        var tts = new RecordingTtsProvider();
        var service = Create(uow, tts: tts);

        var result = await service.ProcessNextAsync();
        Assert.True(result.Success);

        Assert.Equal(StoryStatus.Ready, uow.Items<Story>().Single().Status);
        var scenes = uow.Items<StoryScene>().OrderBy(x => x.SceneIndex).ToArray();
        Assert.Equal(2, scenes.Length);
        Assert.Equal("A\n\nB", string.Concat(scenes.Select(x => x.SceneText)));
        Assert.Equal(4, uow.Items<MediaAsset>().Count);
        Assert.All(scenes, scene => Assert.Equal(2,
            uow.Items<MediaAsset>().Count(asset => asset.StorySceneId == scene.Id && asset.Status == MediaStatus.Ready)));
        Assert.Equal(scenes.Select(x => x.SceneText), tts.Inputs);
    }

    [Fact]
    public async Task ProcessNext_RetryDoesNotDuplicateLogicalAssets()
    {
        var uow = Seed();
        var image = new FlakyImageProvider();
        var service = Create(uow, image: image);

        var result = await service.ProcessNextAsync();
        Assert.True(result.Success);

        Assert.Equal(3, image.Calls); // first scene retries once, then second scene succeeds
        Assert.Equal(4, uow.Items<MediaAsset>().Count);
        Assert.Equal(4, uow.Items<MediaAsset>().Select(x => (x.StorySceneId, x.Type)).Distinct().Count());
    }

    [Fact]
    public async Task ProcessNext_ImageValidationFailureNeverMarksStoryReady()
    {
        var uow = Seed();
        var finalizer = new RecordingFinalizer();
        var service = Create(uow, evaluator: new RejectingEvaluator(), finalizer: finalizer);

        var result = await service.ProcessNextAsync();
        Assert.False(result.Success);
        Assert.True(result.IsPermanentFailure);

        Assert.Equal(StoryStatus.MediaProcessing, uow.Items<Story>().Single().Status);
        Assert.DoesNotContain(uow.Items<MediaAsset>(), x => x.Status == MediaStatus.Ready);
        Assert.Equal("ILLUSTRATION_RETRY_EXHAUSTED", finalizer.ErrorCode);
    }

    [Fact]
    public async Task ProcessNext_ReusesPersistedMediaContextRevision()
    {
        var uow = Seed();
        uow.Seed(new MediaContext { Id = 90, StoryVersionId = 10, Revision = 2, ContextJson = "{\"revision\":2}" });
        var service = Create(uow);

        await service.ProcessNextAsync();

        Assert.Single(uow.Items<MediaContext>());
        Assert.Equal(2, uow.Items<MediaContext>().Single().Revision);
    }

    [Fact]
    public async Task ProcessNext_UnconfiguredProvider_IsClassifiedAsPermanentFailure()
    {
        var uow = Seed();
        var finalizer = new RecordingFinalizer();
        var service = Create(uow, image: new UnavailableImageGenerationProvider(), finalizer: finalizer);

        var result = await service.ProcessNextAsync();

        Assert.False(result.Success);
        Assert.True(result.IsPermanentFailure);
        Assert.Equal("IMAGE_PROVIDER_NOT_CONFIGURED", result.ErrorCode);
        Assert.Equal(result.ErrorCode, finalizer.ErrorCode);
    }

    [Fact]
    public async Task ProcessNext_UnconfiguredEvaluator_FailsWithConfigurationError()
    {
        var uow = Seed();
        var service = Create(uow, evaluator: new FailClosedMediaEvaluator());

        var result = await service.ProcessNextAsync();

        Assert.False(result.Success);
        Assert.True(result.IsPermanentFailure);
        Assert.Equal("MEDIA_EVALUATOR_NOT_CONFIGURED", result.ErrorCode);
        Assert.Equal(StoryStatus.MediaProcessing, uow.Items<Story>().Single().Status);
    }

    [Fact]
    public async Task ProcessNext_TransientProviderError_IsEligibleForQuickRetry()
    {
        var uow = Seed();
        var service = Create(uow, image: new AlwaysTransientImageProvider());

        var result = await service.ProcessNextAsync();

        Assert.False(result.Success);
        Assert.False(result.IsPermanentFailure);
        Assert.Equal("ILLUSTRATION_RETRY_EXHAUSTED", result.ErrorCode);
    }

    [Fact]
    public async Task ProcessNext_ContextRemovedBeforePersist_RejectsResultAsStale()
    {
        var uow = Seed();
        var finalizer = new RecordingFinalizer();
        var service = Create(uow, image: new ContextRemovingImageProvider(uow), finalizer: finalizer);

        var result = await service.ProcessNextAsync();

        Assert.False(result.Success);
        Assert.False(result.IsPermanentFailure);
        Assert.Equal("STALE_MEDIA_RESULT", result.ErrorCode);
        Assert.Equal(StoryStatus.MediaProcessing, uow.Items<Story>().Single().Status);
    }

    [Fact]
    public async Task ProcessNext_InvalidStoryState_IsPermanentInsteadOfReportedAsNoJob()
    {
        var uow = Seed();
        uow.Items<Story>().Single().Status = StoryStatus.Archived;
        var finalizer = new RecordingFinalizer();
        var service = Create(uow, finalizer: finalizer);

        var result = await service.ProcessNextAsync();

        Assert.True(result.JobFound);
        Assert.False(result.Success);
        Assert.True(result.IsPermanentFailure);
        Assert.Equal("STALE_MEDIA_RESULT", result.ErrorCode);
        Assert.Equal(result.ErrorCode, finalizer.ErrorCode);
    }

    private static MediaGenerationService Create(
        StoryReviewServiceTests.FakeUnitOfWork uow,
        IImageGenerationProvider? image = null,
        ITtsProvider? tts = null,
        IMediaAlignmentEvaluator? evaluator = null,
        RecordingFinalizer? finalizer = null) => new(
        uow,
        new MediaContextBuilder(),
        new StoryBlockParser(),
        new ParagraphSceneSegmentationProvider(),
        new SceneCoverageValidator(),
        new SceneSpecificationBuilder(),
        image ?? new PassingImageProvider(),
        tts ?? new RecordingTtsProvider(),
        evaluator ?? new PassingEvaluator(),
        evaluator as IMediaSafetyEvaluator ?? new PassingEvaluator(),
        finalizer ?? new RecordingFinalizer(),
        new MediaGenerationOptions { AssetMaxAttempts = 3, JobLeaseMinutes = 5 });

    private static StoryReviewServiceTests.FakeUnitOfWork Seed()
    {
        var uow = new StoryReviewServiceTests.FakeUnitOfWork();
        uow.Seed(new Story { Id = 1, AuthorUserId = 1, ChildProfileId = 1, Status = StoryStatus.Approved });
        uow.Seed(new StoryVersion
        {
            Id = 10, StoryId = 1, VersionNo = 1, IsCurrent = true, Title = "Story", Content = "A\n\nB"
        });
        uow.Seed(new StoryGenerationJob
        {
            Id = 20, StoryId = 1, StoryVersionId = 10, BaseStoryVersionId = 10,
            Operation = GenerationJobOperation.GenerateMediaPackage, Stage = JobStage.MediaPending,
            Status = GenerationJobStatus.Pending, MaxAttempts = 3, ConcurrencyToken = "initial"
        });
        return uow;
    }

    private sealed class PassingImageProvider : IImageGenerationProvider
    {
        public Task<GeneratedIllustration> GenerateAsync(SceneSpecification specification, CancellationToken cancellationToken = default) =>
            Task.FromResult(new GeneratedIllustration($"https://media.test/{specification.StorySceneId}.png"));
    }

    private sealed class FlakyImageProvider : IImageGenerationProvider
    {
        public int Calls { get; private set; }
        public Task<GeneratedIllustration> GenerateAsync(SceneSpecification specification, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Calls == 1) throw new InvalidOperationException("temporary");
            return Task.FromResult(new GeneratedIllustration($"https://media.test/{specification.StorySceneId}.png"));
        }
    }

    private sealed class ContextRemovingImageProvider : IImageGenerationProvider
    {
        private readonly StoryReviewServiceTests.FakeUnitOfWork _unitOfWork;
        public ContextRemovingImageProvider(StoryReviewServiceTests.FakeUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

        public Task<GeneratedIllustration> GenerateAsync(
            SceneSpecification specification, CancellationToken cancellationToken = default)
        {
            var context = _unitOfWork.Items<MediaContext>().Single();
            _unitOfWork.Repository<MediaContext>().Delete(context);
            return Task.FromResult(new GeneratedIllustration("https://media.test/stale.png"));
        }
    }

    private sealed class AlwaysTransientImageProvider : IImageGenerationProvider
    {
        public Task<GeneratedIllustration> GenerateAsync(
            SceneSpecification specification, CancellationToken cancellationToken = default) =>
            throw new TimeoutException("temporary provider timeout");
    }

    private sealed class RecordingTtsProvider : ITtsProvider
    {
        public List<string> Inputs { get; } = [];
        public Task<GeneratedAudio> GenerateAsync(string exactSceneText, CancellationToken cancellationToken = default)
        {
            Inputs.Add(exactSceneText);
            return Task.FromResult(new GeneratedAudio($"https://media.test/{Inputs.Count}.mp3"));
        }
    }

    private sealed class PassingEvaluator : IMediaAlignmentEvaluator, IMediaSafetyEvaluator
    {
        public Task<MediaEvaluationResult> EvaluateAsync(
            SceneSpecification specification, GeneratedIllustration illustration, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MediaEvaluationResult(MediaEvaluationDecision.Pass));
    }

    private sealed class RejectingEvaluator : IMediaAlignmentEvaluator, IMediaSafetyEvaluator
    {
        public Task<MediaEvaluationResult> EvaluateAsync(
            SceneSpecification specification, GeneratedIllustration illustration, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MediaEvaluationResult(MediaEvaluationDecision.Fail, "wrong scene"));
    }

    private sealed class RecordingFinalizer : IMediaGenerationJobFailureFinalizer
    {
        public string? ErrorCode { get; private set; }
        public Task MarkFailedAsync(int jobId, string expectedConcurrencyToken, string errorCode,
            CancellationToken cancellationToken = default)
        {
            ErrorCode = errorCode;
            return Task.CompletedTask;
        }
    }
}
