using StoryPlatform.Application.Features.MediaGeneration;
using StoryPlatform.Application.Features.MediaGeneration.Interfaces;
using StoryPlatform.Application.Features.MediaGeneration.Models;
using StoryPlatform.Application.Features.MediaGeneration.Services;
using StoryPlatform.Application.Features.MediaStorage.Interfaces;
using StoryPlatform.Application.Features.MediaStorage.Models;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.Infrastructure.AI;
using Microsoft.Extensions.Logging.Abstractions;
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
        Assert.Equal(4, storage.UploadedPaths.Count);
        Assert.StartsWith("1/v1/scene-0-beat-1-asset-", storage.UploadedPaths[0]);
        Assert.Equal("1/v1/audio-s0-1-a1.mp3", storage.UploadedPaths[1]);
        Assert.StartsWith("1/v1/scene-1-beat-1-asset-", storage.UploadedPaths[2]);
        Assert.Equal("1/v1/audio-s1-1-a1.mp3", storage.UploadedPaths[3]);
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
        // Attempt 1 succeeds with a distinct beat/asset/generation path.
        Assert.Contains(storage.UploadedPaths, p => p.Contains("scene-0-beat-1-") && p.EndsWith("-a2.png"));
        Assert.Contains(storage.UploadedPaths, p => p.Contains("scene-1-beat-1-") && p.EndsWith("-a1.png"));

        // Single path per scene after retry — no silent overwrite.
        var scene0Paths = storage.UploadedPaths.Where(p => p.Contains("scene-0")).ToArray();
        Assert.Single(scene0Paths);
        Assert.EndsWith("-a2.png", scene0Paths[0]);
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

    [Fact]
    public async Task ProcessNext_TwoBeatsInOneParagraph_ProducesTwoImagesAndPreservesAudioText()
    {
        var uow = Seed();
        var text = "The fox jumps and waves.\n\nThe rabbit smiles.";
        uow.Items<StoryVersion>().Single().Content = text;
        uow.Seed(new StoryScene
        {
            Id = 30, StoryVersionId = 10, SceneIndex = 0, SceneText = text,
            TextRangeStart = 0, TextRangeEnd = text.Length
        });
        var tts = new RecordingTtsProvider();
        var service = Create(uow, tts: tts, planner: new FixedBeatPlanner([
            new(1, 0, 13, "The fox jumps"),
            new(2, 14, 24, "The fox waves")]));

        var result = await service.ProcessNextAsync();

        Assert.True(result.Success);
        Assert.Equal(StoryStatus.Ready, uow.Items<Story>().Single().Status);
        Assert.Equal(2, uow.Items<IllustrationBeat>().Count);
        Assert.Equal(2, uow.Items<MediaAsset>().Count(x => x.Type == MediaType.Illustration));
        Assert.Equal(new[] { "The fox jumps and waves.", "The rabbit smiles." }, tts.Inputs);
        var package = await service.GetPackageAsync(1, 1);
        Assert.Equal(new[] { 1, 2 }, package.Scenes.Single().Illustrations.Select(x => x.BeatOrder));
        Assert.Equal(2, (await service.GetProgressAsync(1, 1)).RequiredIllustrations);
    }

    [Fact]
    public async Task ProcessNext_InvalidBeatPlan_FallsBackToOneSceneImage()
    {
        var uow = Seed();
        var service = Create(uow, planner: new FixedBeatPlanner([
            new(1, 0, 1, "A"), new(2, 0, 1, "B"),
            new(3, 0, 1, "C"), new(4, 0, 1, "D")]));

        Assert.True((await service.ProcessNextAsync()).Success);
        Assert.Equal(2, uow.Items<IllustrationBeat>().Count); // one fallback beat for each scene
    }

    [Fact]
    public async Task ProcessNext_InvalidBeatOffset_FallsBackToWholeSceneImage()
    {
        var uow = Seed();
        var service = Create(uow, planner: new FixedBeatPlanner([new(1, 0, 999, "Outside text")]));

        Assert.True((await service.ProcessNextAsync()).Success);
        Assert.All(uow.Items<IllustrationBeat>(), beat =>
        {
            Assert.Equal(0, beat.StartOffset);
            Assert.Equal(uow.Items<StoryScene>().Single(scene => scene.Id == beat.StorySceneId).SceneText.Length,
                beat.EndOffset);
        });
    }

    [Fact]
    public async Task ProcessNext_ThreeBeats_AllReadyBeforeStoryReady()
    {
        var uow = Seed();
        var text = "The fox jumps, waves, and smiles.";
        uow.Items<StoryVersion>().Single().Content = text;
        uow.Seed(new StoryScene
        {
            Id = 30, StoryVersionId = 10, SceneIndex = 0, SceneText = text,
            TextRangeStart = 0, TextRangeEnd = text.Length
        });
        var service = Create(uow, planner: new FixedBeatPlanner([
            new(1, 0, 13, "Jumping"), new(2, 14, 20, "Waving"), new(3, 21, text.Length, "Smiling")]));

        Assert.True((await service.ProcessNextAsync()).Success);
        Assert.Equal(3, uow.Items<IllustrationBeat>().Count);
        Assert.Equal(3, (await service.GetProgressAsync(1, 1)).ReadyIllustrations);
        Assert.Equal(StoryStatus.Ready, uow.Items<Story>().Single().Status);
    }

    [Fact]
    public async Task ProcessNext_SecondBeatFails_FirstReadyImageCannotCompleteStory()
    {
        var uow = Seed();
        var text = "The fox jumps and waves.";
        uow.Items<StoryVersion>().Single().Content = text;
        uow.Seed(new StoryScene
        {
            Id = 30, StoryVersionId = 10, SceneIndex = 0, SceneText = text,
            TextRangeStart = 0, TextRangeEnd = text.Length
        });
        var service = Create(uow, image: new FailSecondImageProvider(), planner: new FixedBeatPlanner([
            new(1, 0, 13, "Jumping"), new(2, 14, text.Length, "Waving")]));

        Assert.False((await service.ProcessNextAsync()).Success);
        Assert.Equal(StoryStatus.MediaProcessing, uow.Items<Story>().Single().Status);
        Assert.Single(uow.Items<MediaAsset>(), x => x.Type == MediaType.Illustration && x.Status == MediaStatus.Ready);
        Assert.Equal(2, (await service.GetProgressAsync(1, 1)).RequiredIllustrations);
    }

    [Fact]
    public async Task RegenerateBeat_OnlyRecreatesSelectedImageAndUsesNewPath()
    {
        var uow = Seed();
        var image = new CountingImageProvider();
        var storage = new RecordingMediaStorage();
        var tts = new RecordingTtsProvider();
        var service = Create(uow, image: image, storage: storage, tts: tts);
        Assert.True((await service.ProcessNextAsync()).Success);
        var beats = uow.Items<IllustrationBeat>().OrderBy(x => x.Id).ToArray();
        var other = uow.Items<MediaAsset>().Single(x => x.IllustrationBeatId == beats[1].Id);
        var otherUrl = other.Url;
        var firstPath = storage.UploadedPaths.First(p => p.Contains("scene-0-beat-1-"));

        await service.RegenerateIllustrationBeatAsync(1, 1, beats[0].Id);
        Assert.Equal(StoryStatus.MediaProcessing, uow.Items<Story>().Single().Status);
        Assert.True((await service.ProcessNextAsync()).Success);

        Assert.Equal(3, image.Calls);
        Assert.Equal(2, tts.Inputs.Count);
        Assert.Equal(otherUrl, other.Url);
        Assert.NotEqual(firstPath, storage.UploadedPaths.Last(p => p.Contains("scene-0-beat-1-")));
        Assert.Equal(StoryStatus.Ready, uow.Items<Story>().Single().Status);
    }

    [Fact]
    public async Task RegenerateBeat_FromAnotherStory_IsRejected()
    {
        var uow = Seed();
        var service = Create(uow);
        Assert.True((await service.ProcessNextAsync()).Success);
        uow.Seed(new StoryScene { Id = 90, StoryVersionId = 99, SceneText = "Other", TextRangeEnd = 5 });
        uow.Seed(new IllustrationBeat
        {
            Id = 91, StorySceneId = 90, BeatOrder = 1, StartOffset = 0,
            EndOffset = 5, VisualFocus = "Other"
        });
        await Assert.ThrowsAnyAsync<Exception>(() => service.RegenerateIllustrationBeatAsync(1, 1, 91));
        Assert.Equal(StoryStatus.Ready, uow.Items<Story>().Single().Status);
    }

    [Fact]
    public async Task RegenerateBeat_FromOldVersion_IsRejected()
    {
        var uow = Seed();
        var service = Create(uow);
        Assert.True((await service.ProcessNextAsync()).Success);
        var current = uow.Items<StoryVersion>().Single();
        current.IsCurrent = false;
        uow.Seed(new StoryVersion { Id = 99, StoryId = 1, VersionNo = 2, IsCurrent = true, Content = "New" });
        var oldBeatId = uow.Items<IllustrationBeat>().First().Id;

        await Assert.ThrowsAnyAsync<Exception>(() => service.RegenerateIllustrationBeatAsync(1, 1, oldBeatId));
    }

    [Fact]
    public async Task Readiness_TwoBeats_RequiresBothImagesAndAudio()
    {
        var uow = new StoryReviewServiceTests.FakeUnitOfWork();
        uow.Seed(new StoryScene { Id = 30, StoryVersionId = 10, SceneIndex = 0, SceneText = "A B", TextRangeEnd = 3 });
        uow.Seed(new IllustrationBeat { Id = 31, StorySceneId = 30, BeatOrder = 1, StartOffset = 0, EndOffset = 1, VisualFocus = "A" });
        uow.Seed(new IllustrationBeat { Id = 32, StorySceneId = 30, BeatOrder = 2, StartOffset = 2, EndOffset = 3, VisualFocus = "B" });
        uow.Seed(new StorySegment { Id = 33, StorySceneId = 30, SegmentOrder = 1, StartOffset = 0, EndOffset = 3, TextContent = "A B" });
        uow.Seed(new MediaAsset { Id = 40, StoryVersionId = 10, StorySceneId = 30, IllustrationBeatId = 31,
            Type = MediaType.Illustration, Status = MediaStatus.Ready, ValidationStatus = ValidationStatus.Passed });
        uow.Seed(new MediaAsset { Id = 41, StoryVersionId = 10, StorySceneId = 30, IllustrationBeatId = 32,
            Type = MediaType.Illustration, Status = MediaStatus.Failed });
        uow.Seed(new MediaAsset { Id = 42, StoryVersionId = 10, StorySceneId = 30, StorySegmentId = 33,
            Type = MediaType.TtsAudio, Status = MediaStatus.Ready });
        var readiness = new MediaReadinessService(uow);

        var incomplete = await readiness.CheckAsync(10, 1);
        Assert.False(incomplete.IsReady);
        Assert.Equal(2, incomplete.RequiredIllustrations);
        Assert.Equal(1, incomplete.ReadyIllustrations);
        Assert.Equal(new[] { 32 }, incomplete.MissingIllustrationBeatIds);

        var second = uow.Items<MediaAsset>().Single(x => x.Id == 41);
        second.Status = MediaStatus.Ready;
        second.ValidationStatus = ValidationStatus.Passed;
        Assert.True((await readiness.CheckAsync(10, 1)).IsReady);
    }

    [Fact]
    public async Task ProcessNext_VersionChangesAfterFinalUpload_CannotFinalizeReady()
    {
        var uow = Seed();
        var service = Create(uow, storage: new SwitchVersionAfterLastAudioStorage(uow));

        var result = await service.ProcessNextAsync();

        Assert.False(result.Success);
        Assert.Equal("STALE_MEDIA_RESULT", result.ErrorCode);
        Assert.Equal(StoryStatus.MediaProcessing, uow.Items<Story>().Single().Status);
        Assert.NotEqual(GenerationJobStatus.Completed, uow.Items<StoryGenerationJob>().Single().Status);
    }

    private static MediaGenerationService Create(
        StoryReviewServiceTests.FakeUnitOfWork uow,
        IImageGenerationProvider? image = null,
        ITtsProvider? tts = null,
        IMediaStorage? storage = null,
        IMediaAlignmentEvaluator? evaluator = null,
        IAudioQualityGate? audioGate = null,
        RecordingFinalizer? finalizer = null,
        IIllustrationBeatPlanner? planner = null)
    {
        var evaluatorImpl = evaluator ?? new PassingEvaluator();
        return new MediaGenerationService(
            uow,
            new MediaContextBuilder(),
            new StoryBlockParser(),
            new ParagraphSceneSegmentationProvider(),
            new SceneCoverageValidator(),
            new SceneSpecificationBuilder(),
            planner ?? new SingleBeatPlanner(),
            image ?? new PassingImageProvider(),
            tts ?? new RecordingTtsProvider(),
            storage ?? new RecordingMediaStorage(),
            (IMediaAlignmentEvaluator)evaluatorImpl,
            (IMediaSafetyEvaluator)evaluatorImpl,
            audioGate ?? new PassingAudioQualityGate(),
            new StorySegmentService(),
            finalizer ?? new RecordingFinalizer(),
            new MediaReadinessService(uow),
            new MediaGenerationOptions { AssetMaxAttempts = 3, JobLeaseMinutes = 5 },
            NullLogger<MediaGenerationService>.Instance);
    }

    private sealed class SingleBeatPlanner : IIllustrationBeatPlanner
    {
        public Task<IReadOnlyList<IllustrationBeatSelection>> PlanAsync(
            IllustrationBeatPlanRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<IllustrationBeatSelection>>([
                new(1, 0, request.SceneText.Length, "Main visual moment")]);
    }

    private sealed class FixedBeatPlanner(IReadOnlyList<IllustrationBeatSelection> beats) : IIllustrationBeatPlanner
    {
        public Task<IReadOnlyList<IllustrationBeatSelection>> PlanAsync(
            IllustrationBeatPlanRequest request, CancellationToken cancellationToken = default) => Task.FromResult(beats);
    }

    private sealed class CountingImageProvider : IImageGenerationProvider
    {
        public int Calls { get; private set; }
        public Task<GeneratedMedia> GenerateAsync(SceneSpecification specification, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new GeneratedMedia([1, 2, 3], "image/png"));
        }
    }

    private sealed class FailSecondImageProvider : IImageGenerationProvider
    {
        private int _calls;
        public Task<GeneratedMedia> GenerateAsync(SceneSpecification specification, CancellationToken cancellationToken = default)
        {
            if (++_calls > 1) throw new PermanentMediaGenerationException("IMAGE_PROVIDER_TEST_FAILURE");
            return Task.FromResult(new GeneratedMedia([1, 2, 3], "image/png"));
        }
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

    private sealed class SwitchVersionAfterLastAudioStorage(StoryReviewServiceTests.FakeUnitOfWork uow) : IMediaStorage
    {
        public Task<string> UploadAsync(string storagePath, Stream content, string mimeType,
            CancellationToken cancellationToken = default)
        {
            if (storagePath.Contains("audio-s1-1-", StringComparison.Ordinal))
                uow.Items<StoryVersion>().Single().IsCurrent = false;
            return Task.FromResult(storagePath);
        }

        public Task<string> GetSignedUrlAsync(string storagePath, TimeSpan expiry,
            CancellationToken cancellationToken = default) => Task.FromResult(storagePath);

        public Task DeleteAsync(string storagePath, CancellationToken cancellationToken = default) => Task.CompletedTask;
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
