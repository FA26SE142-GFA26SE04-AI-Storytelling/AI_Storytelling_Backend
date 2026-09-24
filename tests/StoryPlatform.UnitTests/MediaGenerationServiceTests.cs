using StoryPlatform.Application.Features.MediaGeneration;
using StoryPlatform.Application.Features.MediaGeneration.Interfaces;
using StoryPlatform.Application.Features.MediaGeneration.Models;
using StoryPlatform.Application.Features.MediaGeneration.Services;
using StoryPlatform.Application.Features.MediaStorage.Interfaces;
using StoryPlatform.Application.Features.MediaStorage.Models;
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
        var storage = new RecordingMediaStorage();
        var service = Create(uow, tts: tts, storage: storage);

        var result = await service.ProcessNextAsync();
        Assert.True(result.Success);

        Assert.Equal(StoryStatus.Ready, uow.Items<Story>().Single().Status);
        var scenes = uow.Items<StoryScene>().OrderBy(x => x.SceneIndex).ToArray();
        Assert.Equal(2, scenes.Length);
        Assert.Equal("A\n\nB", string.Concat(scenes.Select(x => x.SceneText)));
        Assert.Equal(4, uow.Items<MediaAsset>().Count);
        Assert.All(scenes, scene => Assert.Equal(2,
            uow.Items<MediaAsset>().Count(asset => asset.StorySceneId == scene.Id && asset.Status == MediaStatus.Ready)));
        // Phase 5: audio is generated per-segment (one paragraph per scene → one segment per scene).
        Assert.Equal(new[] { "A", "B" }, tts.Inputs);
        Assert.Equal(
            new[] { "1/v1/scene-0-a1.png", "1/v1/audio-s0-1-a1.mp3", "1/v1/scene-1-a1.png", "1/v1/audio-s1-1-a1.mp3" },
            storage.UploadedPaths);
        Assert.All(uow.Items<MediaAsset>(), asset => Assert.DoesNotContain("://", asset.Url));
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
    public async Task ProcessNext_IllustrationRetry_UploadsToDistinctAttemptPath()
    {
        var uow = Seed();
        var image = new FlakyImageProvider();
        var storage = new RecordingMediaStorage();
        var service = Create(uow, image: image, storage: storage);

        var result = await service.ProcessNextAsync();
        Assert.True(result.Success);

        // First scene's attempt 0 throws "temporary" before upload (no path recorded).
        // Attempt 1 succeeds, uploads to scene-0-a2.png.
        // Second scene's attempt 0 succeeds, uploads to scene-1-a1.png.
        Assert.DoesNotContain("1/v1/scene-0-a1.png", storage.UploadedPaths);
        Assert.Contains("1/v1/scene-0-a2.png", storage.UploadedPaths);
        Assert.Contains("1/v1/scene-1-a1.png", storage.UploadedPaths);

        // Single path per scene after retry — no silent overwrite.
        var scene0Paths = storage.UploadedPaths.Where(p => p.Contains("scene-0")).ToArray();
        Assert.Single(scene0Paths);
        Assert.Equal("1/v1/scene-0-a2.png", scene0Paths[0]);
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
    public async Task ProcessNext_OpenCircuit_StopsAssetRetriesAndReturnsCircuitCode()
    {
        var uow = Seed();
        var image = new OpenCircuitImageProvider();
        var service = Create(uow, image: image);

        var result = await service.ProcessNextAsync();

        Assert.False(result.Success);
        Assert.False(result.IsPermanentFailure);
        Assert.Equal("MEDIA_CIRCUIT_OPEN", result.ErrorCode);
        Assert.Equal(1, image.Calls);
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

    [Fact]
    public async Task ProcessNext_DeterministicAudioFailure_FailsFastWithoutRetrying()
    {
        var uow = Seed();
        var tts = new RecordingTtsProvider();
        var finalizer = new RecordingFinalizer();
        var service = Create(uow, tts: tts, audioGate: new DeterministicFailingAudioGate(), finalizer: finalizer);

        var result = await service.ProcessNextAsync();

        Assert.False(result.Success);
        Assert.True(result.IsPermanentFailure);
        Assert.Single(tts.Inputs); // Fails fast on attempt 1 without retrying
        var audioAsset = uow.Items<MediaAsset>().Single(x => x.Type == MediaType.TtsAudio);
        Assert.Equal(MediaStatus.ManualReview, audioAsset.Status);
        Assert.Equal(ValidationStatus.Failed, audioAsset.ValidationStatus);
        Assert.Equal("TTS_RETRY_EXHAUSTED", result.ErrorCode);
    }

    [Fact]
    public async Task RetryAsync_RequeuesFailedJobAndOnlyResetsUnreadyAssets()
    {
        var uow = Seed();
        var story = uow.Items<Story>().Single();
        story.Status = StoryStatus.MediaProcessing;
        var job = uow.Items<StoryGenerationJob>().Single();
        job.Status = GenerationJobStatus.Failed;
        job.Stage = JobStage.MediaGenerating;
        job.AttemptNo = job.MaxAttempts;
        job.ErrorCode = "ILLUSTRATION_RETRY_EXHAUSTED";
        job.CompletedAt = DateTime.UtcNow;
        uow.Seed(new StoryScene
        {
            Id = 30, StoryVersionId = 10, SceneIndex = 0,
            TextRangeStart = 0, TextRangeEnd = 1, SceneText = "A"
        });
        uow.Seed(new MediaAsset
        {
            Id = 40, StoryVersionId = 10, StorySceneId = 30, Type = MediaType.Illustration,
            Status = MediaStatus.ManualReview, ValidationStatus = ValidationStatus.Failed,
            AttemptCount = 3, LastValidationReason = "temporary failure"
        });
        uow.Seed(new MediaAsset
        {
            Id = 41, StoryVersionId = 10, StorySceneId = 30, Type = MediaType.TtsAudio,
            Status = MediaStatus.Ready, ValidationStatus = ValidationStatus.Passed,
            AttemptCount = 1, Url = "ready.mp3", CompletedAt = DateTime.UtcNow
        });
        var service = Create(uow);

        var progress = await service.RetryAsync(1, 1);

        Assert.Equal(GenerationJobStatus.Pending, job.Status);
        Assert.Equal(JobStage.MediaPending, job.Stage);
        Assert.Equal(0, job.AttemptNo);
        Assert.Null(job.ErrorCode);
        Assert.Null(job.CompletedAt);
        var illustration = uow.Items<MediaAsset>().Single(x => x.Id == 40);
        Assert.Equal(MediaStatus.Queued, illustration.Status);
        Assert.Equal(ValidationStatus.Pending, illustration.ValidationStatus);
        Assert.Equal(0, illustration.AttemptCount);
        Assert.Null(illustration.LastValidationReason);
        var audio = uow.Items<MediaAsset>().Single(x => x.Id == 41);
        Assert.Equal(MediaStatus.Ready, audio.Status);
        Assert.Equal("ready.mp3", audio.Url);
        Assert.Equal("Pending", progress.JobStatus);
        Assert.Null(progress.ErrorCode);
    }

    private static MediaGenerationService Create(
        StoryReviewServiceTests.FakeUnitOfWork uow,
        IImageGenerationProvider? image = null,
        ITtsProvider? tts = null,
        IMediaStorage? storage = null,
        IMediaAlignmentEvaluator? evaluator = null,
        IAudioQualityGate? audioGate = null,
        RecordingFinalizer? finalizer = null)
    {
        var evaluatorImpl = evaluator ?? new PassingEvaluator();
        return new MediaGenerationService(
            uow,
            new MediaContextBuilder(),
            new StoryBlockParser(),
            new ParagraphSceneSegmentationProvider(),
            new SceneCoverageValidator(),
            new SceneSpecificationBuilder(),
            image ?? new PassingImageProvider(),
            tts ?? new RecordingTtsProvider(),
            storage ?? new RecordingMediaStorage(),
            (IMediaAlignmentEvaluator)evaluatorImpl,
            (IMediaSafetyEvaluator)evaluatorImpl,
            audioGate ?? new PassingAudioQualityGate(),
            new StorySegmentService(),
            finalizer ?? new RecordingFinalizer(),
            new MediaGenerationOptions { AssetMaxAttempts = 3, JobLeaseMinutes = 5 });
    }

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
        public Task<GeneratedMedia> GenerateAsync(SceneSpecification specification, CancellationToken cancellationToken = default) =>
            Task.FromResult(new GeneratedMedia([1, 2, 3], "image/png"));
    }

    private sealed class FlakyImageProvider : IImageGenerationProvider
    {
        public int Calls { get; private set; }
        public Task<GeneratedMedia> GenerateAsync(SceneSpecification specification, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Calls == 1) throw new InvalidOperationException("temporary");
            return Task.FromResult(new GeneratedMedia([1, 2, 3], "image/png"));
        }
    }

    private sealed class ContextRemovingImageProvider : IImageGenerationProvider
    {
        private readonly StoryReviewServiceTests.FakeUnitOfWork _unitOfWork;
        public ContextRemovingImageProvider(StoryReviewServiceTests.FakeUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

        public Task<GeneratedMedia> GenerateAsync(
            SceneSpecification specification, CancellationToken cancellationToken = default)
        {
            var context = _unitOfWork.Items<MediaContext>().Single();
            _unitOfWork.Repository<MediaContext>().Delete(context);
            return Task.FromResult(new GeneratedMedia([1, 2, 3], "image/png"));
        }
    }

    private sealed class AlwaysTransientImageProvider : IImageGenerationProvider
    {
        public Task<GeneratedMedia> GenerateAsync(
            SceneSpecification specification, CancellationToken cancellationToken = default) =>
            throw new TimeoutException("temporary provider timeout");
    }

    private sealed class OpenCircuitImageProvider : IImageGenerationProvider
    {
        public int Calls { get; private set; }

        public Task<GeneratedMedia> GenerateAsync(
            SceneSpecification specification, CancellationToken cancellationToken = default)
        {
            Calls++;
            throw new TransientMediaGenerationException("MEDIA_CIRCUIT_OPEN");
        }
    }

    private sealed class RecordingTtsProvider : ITtsProvider
    {
        public List<string> Inputs { get; } = [];
        public Task<GeneratedMedia> GenerateAsync(string exactSceneText, CancellationToken cancellationToken = default)
        {
            Inputs.Add(exactSceneText);
            return Task.FromResult(new GeneratedMedia([4, 5, 6], "audio/mpeg"));
        }
    }

    private sealed class PassingEvaluator : IMediaAlignmentEvaluator, IMediaSafetyEvaluator
    {
        public Task<MediaEvaluationResult> EvaluateAsync(
            SceneSpecification specification, GeneratedMedia illustration, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MediaEvaluationResult(MediaEvaluationDecision.Pass));
    }

    private sealed class PassingAudioQualityGate : IAudioQualityGate
    {
        public AudioQualityGateResult Validate(GeneratedMedia audio, IReadOnlyList<TimingMark> expectedMarks) =>
            AudioQualityGateResult.Pass();
    }

    private sealed class DeterministicFailingAudioGate : IAudioQualityGate
    {
        public AudioQualityGateResult Validate(GeneratedMedia audio, IReadOnlyList<TimingMark> expectedMarks) =>
            AudioQualityGateResult.Fail("AUDIO_MAGIC_BYTES_INVALID: UNRECOGNIZED_AUDIO_HEADER");
    }

    private sealed class RejectingEvaluator : IMediaAlignmentEvaluator, IMediaSafetyEvaluator
    {
        public Task<MediaEvaluationResult> EvaluateAsync(
            SceneSpecification specification, GeneratedMedia illustration, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MediaEvaluationResult(MediaEvaluationDecision.Fail, "wrong scene"));
    }

    private sealed class RecordingMediaStorage : IMediaStorage
    {
        public List<string> UploadedPaths { get; } = [];

        public Task<string> UploadAsync(string storagePath, Stream content, string mimeType,
            CancellationToken cancellationToken = default)
        {
            UploadedPaths.Add(storagePath);
            return Task.FromResult(storagePath);
        }

        public Task<string> GetSignedUrlAsync(string storagePath, TimeSpan expiry,
            CancellationToken cancellationToken = default) =>
            Task.FromResult($"https://media.test/{storagePath}?token=test");

        public Task DeleteAsync(string storagePath, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
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
