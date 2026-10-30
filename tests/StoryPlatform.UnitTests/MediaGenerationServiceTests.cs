using StoryPlatform.Application.Features.MediaGeneration;
using StoryPlatform.Application.Features.MediaGeneration.Interfaces;
using StoryPlatform.Application.Features.MediaGeneration.Models;
using StoryPlatform.Application.Features.MediaGeneration.Services;
using StoryPlatform.Application.Features.MediaStorage.Interfaces;
using StoryPlatform.Application.Features.MediaStorage.Models;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.Infrastructure.AI;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed partial class MediaGenerationServiceTests
{
    [Fact]
    public async Task ManualReadySubmitsTheSameVersionToModeratorWithoutPublishing()
    {
        var (store, confirm, _) = CreatorManualMediaTests.Setup();
        await confirm.ConfirmAsync(1, 1, new(10, "rev1"));
        Assert.True((await Create(store).RunPackageAsync()).Success);
        var queue = new Moq.Mock<StoryPlatform.Application.Features.Moderation.Interfaces.IModerationQueueService>();
        queue.Setup(x => x.EnqueueAsync(Moq.It.IsAny<StoryPlatform.Application.Features.Moderation.DTOs.EnqueueModerationItemCommand>(), Moq.It.IsAny<CancellationToken>()))
            .ReturnsAsync((StoryPlatform.Application.Features.Moderation.DTOs.EnqueueModerationItemCommand cmd, CancellationToken _) =>
                new ModerationItem
                {
                    Id = 99,
                    ItemType = cmd.ItemType,
                    StoryId = cmd.StoryId,
                    StoryVersionId = cmd.StoryVersionId,
                    CreatedByUserId = cmd.CreatedByUserId,
                    Status = ModerationItemStatus.Pending
                });
        var evaluator = new Moq.Mock<StoryPlatform.Application.Features.Moderation.IGrayZoneEvaluator>();
        evaluator.SetupGet(x => x.IsConfigured).Returns(true);
        evaluator.Setup(x => x.CanReuseAsync(Moq.It.IsAny<StoryPlatform.Application.Features.Moderation.GrayZoneAssessment>(), Moq.It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var submission = new StoryPlatform.Application.Features.Moderation.Services.PublicStorySubmissionService(store, queue.Object,
            new MediaReadinessService(store), new Moq.Mock<StoryPlatform.Application.Features.AuditLogs.Interfaces.IAuditLogWriter>().Object, evaluator.Object);
        await Assert.ThrowsAsync<StoryPlatform.Application.Common.Exceptions.BadRequestException>(() =>
            submission.SubmitAsync(1, new() { StoryId = 1, StoryVersionId = 10 }));
        await Create(store).ConfirmAsync(1, 1, new(10, store.Items<StoryVersion>().Single().MediaState.MediaPackageRevision));
        var result = await submission.SubmitAsync(1, new() { StoryId = 1, StoryVersionId = 10 });
        Assert.Equal(10, result.StoryVersionId);
        Assert.Equal(VersionReviewStatus.Pending, store.Items<StoryVersion>().Single().Moderation.ReviewStatus);
        Assert.False(store.Items<Story>().Single().IsPublished);
    }

    [Fact]
    public async Task ConfirmedManualUsesSamePhase5WorkerAndReturnsReadyWithNoGeneratedContent()
    {
        var (store, confirm, _) = CreatorManualMediaTests.Setup();
        await confirm.ConfirmAsync(1, 1, new(10, "rev1"));
        var worker = Create(store);
        Assert.True((await worker.RunPackageAsync()).Success);
        Assert.Equal(StoryStatus.MediaReview, store.Items<Story>().Single().Status);
        Assert.False(store.Items<Story>().Single().IsPublished);
        Assert.False((await worker.GetProgressAsync(1, 1)).IsReady);
        Assert.True((await worker.GetProgressAsync(1, 1)).IsAwaitingConfirmation);
        var package = await worker.GetPackageAsync(1, 1);
        Assert.NotEmpty(package.Scenes);
        Assert.All(package.Scenes, scene => Assert.NotEmpty(scene.Audio));
        Assert.All(package.Scenes.SelectMany(scene => scene.Audio), audio => Assert.NotNull(audio.Url));
        Assert.Contains(store.Items<MediaAsset>(), x => x.Type == MediaType.TtsAudio && x.Status == MediaStatus.Ready);
        Assert.Single(store.Items<StoryVersion>());
        Assert.Empty(store.Items<StoryGenerationRequest>());
        Assert.Null(store.Items<StoryVersion>().Single().OutlineOpening);
        Assert.Equal("Na giúp bạn sửa đồ chơi.", store.Items<StoryVersion>().Single().Content);
        // A later manual draft must not report an old version's media as its own.
        store.Items<StoryVersion>().Single().IsCurrent = false;
        store.Seed(new StoryVersion { Id = 11, StoryId = 1, IsCurrent = true, Content = "Bản mới" });
        store.Items<Story>().Single().Status = StoryStatus.Draft;
        var progress = await worker.GetProgressAsync(1, 1);
        Assert.Equal("NotStarted", progress.JobStatus);
        Assert.False(progress.IsReady);
        await Assert.ThrowsAsync<StoryPlatform.Application.Common.Exceptions.ConflictException>(() => worker.RetryAsync(1, 1));
    }

    [Fact]
    public async Task ManualPhase5RetryUsesTheSameJobAndPinnedContext()
    {
        var (store, confirm, _) = CreatorManualMediaTests.Setup();
        await confirm.ConfirmAsync(1, 1, new(10, "rev1"));
        var job = Assert.Single(store.Items<StoryGenerationJob>(), x => x.Operation == GenerationJobOperation.GenerateMediaPackage);
        job.Status = GenerationJobStatus.Failed;
        var context = Assert.Single(store.Items<MediaContext>()).ContextJson;
        var worker = Create(store);
        await worker.RetryAsync(1, 1);
        Assert.True((await worker.RunPackageAsync()).Success);
        Assert.Same(job, Assert.Single(store.Items<StoryGenerationJob>(), x => x.Operation == GenerationJobOperation.GenerateMediaPackage));
        Assert.Equal(context, Assert.Single(store.Items<MediaContext>()).ContextJson);
        Assert.Equal(StoryStatus.MediaReview, store.Items<Story>().Single().Status);
    }

    [Theory]
    [InlineData(VersionReviewStatus.Pending)]
    [InlineData(VersionReviewStatus.Approved)]
    [InlineData(VersionReviewStatus.ChangesRequested)]
    public async Task Creator_SubmittedMediaCannotBeRegeneratedOrLoseItsUrl(VersionReviewStatus reviewStatus)
    {
        var store = Seed();
        var story = store.Items<Story>().Single();
        story.Scope = StoryScope.Creator; story.Source = StorySource.Ai; story.ChildProfileId = null;
        store.Items<UserAccount>().Single().Role = UserRole.ContentCreator;
        var service = Create(store);
        Assert.True((await service.RunPackageAsync()).Success);
        var version = store.Items<StoryVersion>().Single();
        version.Moderation.ReviewStatus = reviewStatus;
        story.IsPublished = reviewStatus == VersionReviewStatus.Approved;
        var before = store.Items<MediaAsset>().Select(x => (x.Id, x.Url, x.Status)).ToArray();
        var beat = store.Items<IllustrationBeat>().First();
        await Assert.ThrowsAsync<StoryPlatform.Application.Common.Exceptions.ConflictException>(() =>
            service.RegenerateIllustrationBeatAsync(1, story.Id, beat.Id, RegenerationRequest(store)));
        Assert.Equal(before, store.Items<MediaAsset>().Select(x => (x.Id, x.Url, x.Status)).ToArray());
        Assert.Equal(StoryStatus.MediaReview, story.Status);
    }

    [Fact]
    public async Task Creator_MediaReachesReadyWithoutPublishingAndRejectsAnotherAuthor()
    {
        var store = Seed();
        var story = store.Items<Story>().Single();
        story.Scope = StoryScope.Creator; story.Source = StorySource.Ai; story.ChildProfileId = null;
        story.Status = StoryStatus.MediaProcessing;
        store.Items<UserAccount>().Single().Role = UserRole.ContentCreator;
        var service = Create(store);
        Assert.True((await service.RunPackageAsync()).Success);
        Assert.Equal(StoryStatus.MediaReview, story.Status);
        Assert.False(story.IsPublished);
        await service.GetProgressAsync(1, 1);
        await Assert.ThrowsAsync<StoryPlatform.Application.Common.Exceptions.NotFoundException>(() => service.GetProgressAsync(99, 1));
    }

    [Fact]
    public async Task ProcessNext_CreatesExactScenesAndTwoAssetsPerSceneThenMarksReady()
    {
        var uow = Seed();
        var tts = new RecordingTtsProvider();
        var storage = new RecordingMediaStorage();
        var service = Create(uow, tts: tts, storage: storage);

        var result = await service.RunPackageAsync();
        Assert.True(result.Success);

        Assert.Equal(StoryStatus.MediaReview, uow.Items<Story>().Single().Status);
        var scenes = uow.Items<StoryScene>().OrderBy(x => x.SceneIndex).ToArray();
        Assert.Equal(2, scenes.Length);
        Assert.Equal("A\n\nB", string.Concat(scenes.Select(x => x.SceneText)));
        Assert.Equal(4, uow.Items<MediaAsset>().Count);
        Assert.All(scenes, scene => Assert.Equal(2,
            uow.Items<MediaAsset>().Count(asset => asset.StorySceneId == scene.Id && asset.Status == MediaStatus.Ready)));
        // Phase 5: audio is generated per-segment (one paragraph per scene → one segment per scene).
        Assert.Equal(new[] { "A", "B" }, tts.Inputs);
        Assert.Equal(4, storage.UploadedPaths.Count);
        Assert.StartsWith("1/versions/10/v1/scene-0-beat-1-asset-", storage.UploadedPaths[0]);
        Assert.Contains(storage.UploadedPaths, x => x.StartsWith("1/versions/10/v1/audio-s0-1-asset-"));
        Assert.Contains(storage.UploadedPaths, x => x.StartsWith("1/versions/10/v1/scene-1-beat-1-asset-"));
        Assert.Contains(storage.UploadedPaths, x => x.StartsWith("1/versions/10/v1/audio-s1-1-asset-"));
        Assert.All(uow.Items<MediaAsset>(), asset => Assert.DoesNotContain("://", asset.Url));
    }

    [Fact]
    public async Task ManualVersionsKeepSeparateAudioFiles()
    {
        var (store, confirm, _) = CreatorManualMediaTests.Setup();
        var storage = new RecordingMediaStorage();
        var worker = Create(store, storage: storage);
        await confirm.ConfirmAsync(1, 1, new(10, "rev1"));
        Assert.True((await worker.RunPackageAsync()).Success);
        var firstAudio = store.Items<MediaAsset>().Where(x => x.Type == MediaType.TtsAudio).Select(x => x.Url).ToArray();
        Assert.NotEmpty(firstAudio);
        var oldVersion = store.Items<StoryVersion>().Single();
        oldVersion.IsCurrent = false;
        store.Seed(new StoryVersion
        {
            Id = 11,
            StoryId = 1,
            IsCurrent = true,
            VersionNo = 2,
            Title = oldVersion.Title,
            Content = "Na cùng bạn trồng cây.",
            AuthoringMetadata = new StoryVersionAuthoringMetadata
            {
                SourceDeclaration = oldVersion.AuthoringMetadata.SourceDeclaration,
                RightsDeclaration = oldVersion.AuthoringMetadata.RightsDeclaration
            }
        });
        var story = store.Items<Story>().Single();
        story.Status = StoryStatus.Draft;
        story.AuthoringRevisionToken = "rev2";
        await confirm.ConfirmAsync(1, 1, new(11, "rev2"));
        Assert.True((await worker.RunPackageAsync()).Success);
        var package = await worker.GetPackageAsync(1, 1);
        Assert.All(package.Scenes.SelectMany(x => x.Audio), audio =>
        {
            Assert.Contains("/versions/11/", audio.Url);
            Assert.DoesNotContain(audio.Url, firstAudio);
        });
        Assert.Equal(storage.UploadedPaths.Count, storage.UploadedPaths.Distinct().Count());
    }

    [Fact]
    public async Task ManualMediaRejectsAnAutomationGenerationRequest()
    {
        var (store, confirm, _) = CreatorManualMediaTests.Setup();
        await confirm.ConfirmAsync(1, 1, new(10, "rev1"));
        store.Items<StoryGenerationJob>().Single().GenerationRequestId = 123;
        var storage = new RecordingMediaStorage();
        var result = await Create(store, storage: storage).RunPackageAsync();
        Assert.False(result.Success);
        Assert.Equal("INVALID_MEDIA_HANDOFF", result.ErrorCode);
        Assert.Empty(storage.UploadedPaths);
    }

    [Fact]
    public async Task ProcessNext_RetryDoesNotDuplicateLogicalAssets()
    {
        var uow = Seed();
        var image = new FlakyImageProvider();
        var service = Create(uow, image: image);

        var result = await service.RunPackageAsync();
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

        var result = await service.RunPackageAsync();
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

        var result = await service.RunPackageAsync();
        Assert.False(result.Success);
        Assert.True(result.IsPermanentFailure);

        Assert.Equal(StoryStatus.MediaProcessing, uow.Items<Story>().Single().Status);
        Assert.DoesNotContain(uow.Items<MediaAsset>(), x => x.Type == MediaType.Illustration && x.Status == MediaStatus.Ready);
        Assert.Equal("ILLUSTRATION_RETRY_EXHAUSTED", finalizer.ErrorCode);
    }

    [Fact]
    public async Task ProcessNext_ReusesPersistedMediaContextRevision()
    {
        var uow = Seed();
        uow.Seed(new MediaContext { Id = 90, StoryVersionId = 10, Revision = 2, ContextJson = "{\"revision\":2}" });
        var service = Create(uow);

        await service.RunPackageAsync();

        Assert.Single(uow.Items<MediaContext>());
        Assert.Equal(2, uow.Items<MediaContext>().Single().Revision);
    }

    [Fact]
    public async Task ProcessNext_UnconfiguredProvider_IsClassifiedAsPermanentFailure()
    {
        var uow = Seed();
        var finalizer = new RecordingFinalizer();
        var service = Create(uow, image: new UnavailableImageGenerationProvider(), finalizer: finalizer);

        var result = await service.RunPackageAsync();

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

        var result = await service.RunPackageAsync();

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

        var result = await service.RunPackageAsync();

        Assert.False(result.Success);
        Assert.False(result.IsPermanentFailure);
        Assert.Equal("MEDIA_PROVIDER_TIMEOUT", result.ErrorCode);
    }

    [Fact]
    public async Task ProcessNext_OpenCircuit_StopsAssetRetriesAndReturnsCircuitCode()
    {
        var uow = Seed();
        var image = new OpenCircuitImageProvider();
        var service = Create(uow, image: image);

        var result = await service.RunPackageAsync();

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

        var result = await service.RunPackageAsync();

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

        var result = await service.RunPackageAsync();

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

        var result = await service.RunPackageAsync();

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
        var job = uow.Items<StoryGenerationJob>().Single(x => x.Operation == GenerationJobOperation.GenerateMediaPackage);
        job.Status = GenerationJobStatus.Failed;
        job.Stage = JobStage.MediaGenerating;
        job.AttemptNo = job.MaxAttempts;
        job.ErrorCode = "ILLUSTRATION_RETRY_EXHAUSTED";
        job.CompletedAt = DateTime.UtcNow;
        uow.Seed(new StoryScene
        {
            Id = 30,
            StoryVersionId = 10,
            SceneIndex = 0,
            TextRangeStart = 0,
            TextRangeEnd = 1,
            SceneText = "A"
        });
        uow.Seed(new MediaAsset
        {
            Id = 40,
            StoryVersionId = 10,
            StorySceneId = 30,
            Type = MediaType.Illustration,
            Status = MediaStatus.ManualReview,
            ValidationStatus = ValidationStatus.Failed,
            AttemptCount = 3,
            LastValidationReason = "temporary failure"
        });
        uow.Seed(new MediaAsset
        {
            Id = 41,
            StoryVersionId = 10,
            StorySceneId = 30,
            Type = MediaType.TtsAudio,
            Status = MediaStatus.Ready,
            ValidationStatus = ValidationStatus.Passed,
            AttemptCount = 1,
            Url = "ready.mp3",
            CompletedAt = DateTime.UtcNow
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
            Id = 30,
            StoryVersionId = 10,
            SceneIndex = 0,
            SceneText = text,
            TextRangeStart = 0,
            TextRangeEnd = text.Length
        });
        var tts = new RecordingTtsProvider();
        var service = Create(uow, tts: tts, planner: new FixedBeatPlanner([
            new(1, 0, 13, "The fox jumps"),
            new(2, 14, 24, "The fox waves")]));

        var result = await service.RunPackageAsync();

        Assert.True(result.Success);
        Assert.Equal(StoryStatus.MediaReview, uow.Items<Story>().Single().Status);
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

        Assert.True((await service.RunPackageAsync()).Success);
        Assert.Equal(2, uow.Items<IllustrationBeat>().Count); // one fallback beat for each scene
    }

    [Fact]
    public async Task ProcessNext_InvalidBeatOffset_FallsBackToWholeSceneImage()
    {
        var uow = Seed();
        var service = Create(uow, planner: new FixedBeatPlanner([new(1, 0, 999, "Outside text")]));

        Assert.True((await service.RunPackageAsync()).Success);
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
            Id = 30,
            StoryVersionId = 10,
            SceneIndex = 0,
            SceneText = text,
            TextRangeStart = 0,
            TextRangeEnd = text.Length
        });
        var service = Create(uow, planner: new FixedBeatPlanner([
            new(1, 0, 13, "Jumping"), new(2, 14, 20, "Waving"), new(3, 21, text.Length, "Smiling")]));

        Assert.True((await service.RunPackageAsync()).Success);
        Assert.Equal(3, uow.Items<IllustrationBeat>().Count);
        Assert.Equal(3, (await service.GetProgressAsync(1, 1)).ReadyIllustrations);
        Assert.Equal(StoryStatus.MediaReview, uow.Items<Story>().Single().Status);
    }

    [Fact]
    public async Task ProcessNext_SecondBeatFails_FirstReadyImageCannotCompleteStory()
    {
        var uow = Seed();
        var text = "The fox jumps and waves.";
        uow.Items<StoryVersion>().Single().Content = text;
        uow.Seed(new StoryScene
        {
            Id = 30,
            StoryVersionId = 10,
            SceneIndex = 0,
            SceneText = text,
            TextRangeStart = 0,
            TextRangeEnd = text.Length
        });
        var service = Create(uow, image: new FailSecondImageProvider(), planner: new FixedBeatPlanner([
            new(1, 0, 13, "Jumping"), new(2, 14, text.Length, "Waving")]));

        Assert.False((await service.RunPackageAsync()).Success);
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
        Assert.True((await service.RunPackageAsync()).Success);
        var beats = uow.Items<IllustrationBeat>().OrderBy(x => x.Id).ToArray();
        var other = uow.Items<MediaAsset>().Single(x => x.IllustrationBeatId == beats[1].Id);
        var otherUrl = other.Url;
        var firstPath = storage.UploadedPaths.First(p => p.Contains("scene-0-beat-1-"));

        await service.RegenerateIllustrationBeatAsync(1, 1, beats[0].Id, RegenerationRequest(uow));
        Assert.Equal(StoryStatus.MediaProcessing, uow.Items<Story>().Single().Status);
        Assert.True((await service.RunPackageAsync()).Success);

        Assert.Equal(3, image.Calls);
        Assert.Equal(2, tts.Inputs.Count);
        Assert.Equal(otherUrl, other.Url);
        Assert.NotEqual(firstPath, storage.UploadedPaths.Last(p => p.Contains("scene-0-beat-1-")));
        Assert.Equal(StoryStatus.MediaReview, uow.Items<Story>().Single().Status);
    }

    [Fact]
    public async Task RegenerateBeat_FromAnotherStory_IsRejected()
    {
        var uow = Seed();
        var service = Create(uow);
        Assert.True((await service.RunPackageAsync()).Success);
        uow.Seed(new StoryScene { Id = 90, StoryVersionId = 99, SceneText = "Other", TextRangeEnd = 5 });
        uow.Seed(new IllustrationBeat
        {
            Id = 91,
            StorySceneId = 90,
            BeatOrder = 1,
            StartOffset = 0,
            EndOffset = 5,
            VisualFocus = "Other"
        });
        await Assert.ThrowsAnyAsync<Exception>(() => service.RegenerateIllustrationBeatAsync(1, 1, 91, RegenerationRequest(uow)));
        Assert.Equal(StoryStatus.MediaReview, uow.Items<Story>().Single().Status);
    }

    [Fact]
    public async Task RegenerateBeat_FromOldVersion_IsRejected()
    {
        var uow = Seed();
        var service = Create(uow);
        Assert.True((await service.RunPackageAsync()).Success);
        var current = uow.Items<StoryVersion>().Single();
        current.IsCurrent = false;
        uow.Seed(new StoryVersion { Id = 99, StoryId = 1, VersionNo = 2, IsCurrent = true, Content = "New" });
        var oldBeatId = uow.Items<IllustrationBeat>().First().Id;

        await Assert.ThrowsAnyAsync<Exception>(() => service.RegenerateIllustrationBeatAsync(1, 1, oldBeatId, RegenerationRequest(uow)));
    }

    [Fact]
    public async Task Readiness_TwoBeats_RequiresBothImagesAndAudio()
    {
        var uow = new StoryReviewServiceTests.FakeUnitOfWork();
        uow.Seed(new UserAccount { Id = 1, Role = UserRole.User, Status = AccountStatus.Active });
        uow.Seed(new ChildProfile { Id = 1, OwnerUserId = 1, Status = ChildProfileStatus.Active });
        uow.Seed(new SafetyPolicy { Id = 1, ChildProfileId = 1, ConsentRecorded = true, ConsentRecordedAt = DateTime.UtcNow, ConsentPolicyVersion = 1 });
        uow.Seed(new StoryScene { Id = 30, StoryVersionId = 10, SceneIndex = 0, SceneText = "A B", TextRangeEnd = 3 });
        uow.Seed(new IllustrationBeat { Id = 31, StorySceneId = 30, BeatOrder = 1, StartOffset = 0, EndOffset = 1, VisualFocus = "A" });
        uow.Seed(new IllustrationBeat { Id = 32, StorySceneId = 30, BeatOrder = 2, StartOffset = 2, EndOffset = 3, VisualFocus = "B" });
        uow.Seed(new StorySegment { Id = 33, StorySceneId = 30, SegmentOrder = 1, StartOffset = 0, EndOffset = 3, TextContent = "A B" });
        uow.Seed(new MediaAsset
        {
            Id = 40,
            StoryVersionId = 10,
            StorySceneId = 30,
            IllustrationBeatId = 31,
            Type = MediaType.Illustration,
            Status = MediaStatus.Ready,
            ValidationStatus = ValidationStatus.Passed
        });
        uow.Seed(new MediaAsset
        {
            Id = 41,
            StoryVersionId = 10,
            StorySceneId = 30,
            IllustrationBeatId = 32,
            Type = MediaType.Illustration,
            Status = MediaStatus.Failed
        });
        uow.Seed(new MediaAsset
        {
            Id = 42,
            StoryVersionId = 10,
            StorySceneId = 30,
            StorySegmentId = 33,
            Type = MediaType.TtsAudio,
            Status = MediaStatus.Ready
        });
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

        var result = await service.RunPackageAsync();

        Assert.False(result.Success);
        Assert.Equal("STALE_MEDIA_RESULT", result.ErrorCode);
        Assert.Equal(StoryStatus.MediaProcessing, uow.Items<Story>().Single().Status);
        Assert.NotEqual(GenerationJobStatus.Completed, uow.Items<StoryGenerationJob>().Single(x => x.Operation == GenerationJobOperation.GenerateMediaPackage).Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GeneratedPackageWaitsForOwnerConfirmationAndReplayIsIdempotent(bool creator)
    {
        var store = Seed();
        if (creator)
        {
            store.Items<Story>().Single().Scope = StoryScope.Creator;
            store.Items<Story>().Single().ChildProfileId = null;
            store.Items<UserAccount>().Single().Role = UserRole.ContentCreator;
        }
        var service = Create(store);
        Assert.True((await service.RunPackageAsync()).Success);
        var pending = await service.GetProgressAsync(1, 1);
        Assert.True(pending.IsGenerated);
        Assert.True(pending.IsAwaitingConfirmation);
        Assert.False(pending.IsReady);
        Assert.False(pending.IsConfirmed);
        var paths = store.Items<MediaAsset>().Select(x => x.Url).ToArray();
        var result = await service.ConfirmAsync(1, 1, new(10, pending.PackageRevision));
        Assert.True(result.IsReady);
        Assert.True(result.IsConfirmed);
        Assert.Equal(1, result.ConfirmedByUserId);
        Assert.NotNull(result.ConfirmedAt);
        Assert.Equal(result, await service.ConfirmAsync(1, 1, new(10, pending.PackageRevision)));
        Assert.Single(store.Items<StoryVersion>());
        Assert.Single(store.Items<StoryGenerationJob>(), x => x.Operation == GenerationJobOperation.GenerateMediaPackage);
        Assert.Equal(paths, store.Items<MediaAsset>().Select(x => x.Url).ToArray());
        var package = await service.GetPackageAsync(1, 1);
        Assert.All(package.Scenes.SelectMany(x => x.Audio), x => Assert.StartsWith("https://media.test/", x.Url));
    }

    [Fact]
    public async Task ConfirmRejectsStaleVersionRevisionAndActiveJob()
    {
        var (store, queue, _) = CreatorManualMediaTests.Setup();
        await queue.ConfirmAsync(1, 1, new(10, "rev1"));
        var service = Create(store);
        await Assert.ThrowsAsync<StoryPlatform.Application.Common.Exceptions.ConflictException>(() => service.ConfirmAsync(1, 1, new(10, 1)));
        Assert.True((await service.RunPackageAsync()).Success);
        await Assert.ThrowsAsync<StoryPlatform.Application.Common.Exceptions.ConflictException>(() => service.ConfirmAsync(1, 1, new(11, 1)));
        await Assert.ThrowsAsync<StoryPlatform.Application.Common.Exceptions.ConflictException>(() => service.ConfirmAsync(1, 1, new(10, 9)));
        await Assert.ThrowsAsync<StoryPlatform.Application.Common.Exceptions.NotFoundException>(() => service.ConfirmAsync(2, 1, new(10, 1)));
        Assert.Null(store.Items<StoryVersion>().Single().MediaState.MediaConfirmedAt);
    }

    [Fact]
    public async Task ConfirmRejectsIncompleteMediaAndRevokedParentPermission()
    {
        var store = Seed();
        var guard = new Mock<IChildProfileAccessGuard>();
        var service = Create(store, supervision: guard.Object);
        Assert.True((await service.RunPackageAsync()).Success);
        var asset = store.Items<MediaAsset>().First();
        asset.Status = MediaStatus.Failed;
        await Assert.ThrowsAsync<StoryPlatform.Application.Common.Exceptions.ConflictException>(() => service.ConfirmAsync(1, 1, new(10, 1)));
        asset.Status = MediaStatus.Ready;
        guard.Setup(x => x.EnsureOwnerAsync(1, 1, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new StoryPlatform.Application.Common.Exceptions.ForbiddenException());
        await Assert.ThrowsAsync<StoryPlatform.Application.Common.Exceptions.ForbiddenException>(() => service.ConfirmAsync(1, 1, new(10, 1)));
    }

    [Fact]
    public async Task RegenerateAudioInvalidatesConfirmationAndKeepsOtherFiles()
    {
        var store = Seed();
        var storage = new RecordingMediaStorage();
        var service = Create(store, storage: storage);
        Assert.True((await service.RunPackageAsync()).Success);
        await service.ConfirmAsync(1, 1, new(10, 1));
        var target = store.Items<MediaAsset>().First(x => x.Type == MediaType.TtsAudio);
        var oldPath = target.Url;
        var other = store.Items<MediaAsset>().Where(x => x.Id != target.Id).Select(x => x.Url).ToArray();
        await service.RegenerateSegmentAudioAsync(1, 1, target.StorySegmentId!.Value, new(10, 1, true, "regenerate-audio"));
        Assert.Equal(2, store.Items<StoryVersion>().Single().MediaState.MediaPackageRevision);
        Assert.Null(store.Items<StoryVersion>().Single().MediaState.MediaConfirmedAt);
        Assert.True((await service.RunPackageAsync()).Success);
        Assert.Equal(oldPath, target.Url);
        Assert.False(target.IsCurrent);
        Assert.NotEqual(oldPath, store.Items<MediaAsset>().Single(x => x.ReplacesMediaAssetId == target.Id).Url);
        Assert.Equal(other, store.Items<MediaAsset>().Where(x => x.Id != target.Id && x.ReplacesMediaAssetId != target.Id).Select(x => x.Url).ToArray());
        Assert.False((await service.GetProgressAsync(1, 1)).IsReady);
        await Assert.ThrowsAsync<StoryPlatform.Application.Common.Exceptions.ConflictException>(() => service.ConfirmAsync(1, 1, new(10, 1)));
        Assert.True((await service.ConfirmAsync(1, 1, new(10, 2))).IsReady);
    }

    [Fact]
    public async Task LegacyReadyPackageIsPreservedUntilRegeneration()
    {
        var store = Seed();
        var service = Create(store);
        Assert.True((await service.RunPackageAsync()).Success);
        store.Items<Story>().Single().Status = StoryStatus.Ready;
        store.Items<StoryVersion>().Single().MediaState.MediaPackageRevision = 0;
        var progress = await service.GetProgressAsync(1, 1);
        Assert.True(progress.IsReady);
        var beat = store.Items<IllustrationBeat>().First();
        await service.RegenerateIllustrationBeatAsync(1, 1, beat.Id, new(10, 0, true, "regenerate-legacy"));
        Assert.Equal(1, store.Items<StoryVersion>().Single().MediaState.MediaPackageRevision);
        Assert.False((await service.GetProgressAsync(1, 1)).IsReady);
    }

    [Fact]
    public async Task ConcurrentConfirmAndRegenerateCannotApplyToTheSameObservedPackage()
    {
        var store = new ParentAuthoringTestStore();
        store.Items<UserAccount>().Single(x => x.Id == 1).Role = UserRole.ContentCreator;
        store.Items<Story>().Add(new Story { Id = 1, AuthorUserId = 1, Scope = StoryScope.Creator, Source = StorySource.Ai, Status = StoryStatus.MediaReview });
        store.Items<StoryVersion>().Add(new StoryVersion
        {
            Id = 10,
            StoryId = 1,
            IsCurrent = true,
            Content = "Story",
            MediaState = new StoryVersionMediaState
            {
                MediaPackageRevision = 1
            }
        });
        store.Items<StoryGenerationJob>().Add(new StoryGenerationJob
        {
            Id = 20,
            StoryId = 1,
            StoryVersionId = 10,
            Operation = GenerationJobOperation.GenerateMediaPackage,
            Status = GenerationJobStatus.Completed
        });
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var readiness = new Mock<IMediaReadinessService>();
        readiness.Setup(x => x.CheckAsync(10, null, It.IsAny<CancellationToken>())).Returns(async () =>
        {
            entered.TrySetResult();
            await release.Task;
            return new MediaReadinessResult(true, 1, 1, 1, 1, [], [], [], []);
        });
        var service = Create(store.Uow.Object, readiness: readiness.Object);
        var confirmation = service.ConfirmAsync(1, 1, new(10, 1));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var regeneration = Create(store.Uow.Object).RegenerateIllustrationBeatAsync(1, 1, 999, new(10, 1, false, "regenerate-race"));
        release.SetResult();
        Assert.True((await confirmation.WaitAsync(TimeSpan.FromSeconds(10))).IsReady);
        var error = await Assert.ThrowsAsync<StoryPlatform.Application.Common.Exceptions.ConflictException>(() => regeneration.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal("STALE_MEDIA_PACKAGE", error.Message);
        Assert.Equal(StoryStatus.Ready, store.Items<Story>().Single().Status);
    }

    private static RegenerateMediaRequest RegenerationRequest(StoryReviewServiceTests.FakeUnitOfWork store)
    {
        var v = store.Items<StoryVersion>().Single(x => x.IsCurrent);
        return new(v.Id, v.MediaState.MediaPackageRevision, MediaPackageConfirmation.IsConfirmed(v), Guid.NewGuid().ToString("N"));
    }

    internal static MediaGenerationService Create(
        StoryPlatform.Application.Abstractions.Persistence.IUnitOfWork uow,
        IImageGenerationProvider? image = null,
        ITtsProvider? tts = null,
        IMediaStorage? storage = null,
        IMediaAlignmentEvaluator? evaluator = null,
        IAudioQualityGate? audioGate = null,
        RecordingFinalizer? finalizer = null,
        IIllustrationBeatPlanner? planner = null,
        IMediaReadinessService? readiness = null, IChildProfileAccessGuard? supervision = null,
        Microsoft.Extensions.Logging.ILogger<MediaGenerationService>? logger = null, MediaGenerationOptions? options = null)
    {
        var evaluatorImpl = evaluator ?? new PassingEvaluator();
        var clock = new MediaTestPump.Clock();
        var recorder = finalizer ?? new RecordingFinalizer();
        recorder.Uow = uow;
        var service = new MediaGenerationService(
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
            recorder,
            readiness ?? new MediaReadinessService(uow),
            options ?? new MediaGenerationOptions { AssetMaxAttempts = 3, JobLeaseMinutes = 5 },
            logger ?? NullLogger<MediaGenerationService>.Instance, supervision ?? new Mock<IChildProfileAccessGuard>().Object, clock);
        MediaTestPump.Register(service, uow, clock);
        return service;
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
        uow.Seed(new UserAccount { Id = 1, Role = UserRole.User, Status = AccountStatus.Active });
        uow.Seed(new ChildProfile { Id = 1, OwnerUserId = 1, Status = ChildProfileStatus.Active });
        uow.Seed(new SafetyPolicy { Id = 1, ChildProfileId = 1, ConsentRecorded = true, ConsentRecordedAt = DateTime.UtcNow, ConsentPolicyVersion = 1 });
        uow.Seed(new Story { Id = 1, AuthorUserId = 1, ChildProfileId = 1, Status = StoryStatus.Approved });
        uow.Seed(new StoryVersion
        {
            Id = 10,
            StoryId = 1,
            VersionNo = 1,
            IsCurrent = true,
            Title = "Story",
            Content = "A\n\nB"
        });
        uow.Seed(new StoryGenerationJob
        {
            Id = 20,
            StoryId = 1,
            StoryVersionId = 10,
            BaseStoryVersionId = 10,
            Operation = GenerationJobOperation.GenerateMediaPackage,
            Stage = JobStage.MediaPending,
            Status = GenerationJobStatus.Pending,
            MaxAttempts = 3,
            ConcurrencyToken = "initial"
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

    internal sealed class RecordingFinalizer : IMediaGenerationJobFailureFinalizer
    {
        public string? ErrorCode { get; private set; }
        public StoryPlatform.Application.Abstractions.Persistence.IUnitOfWork? Uow { get; set; }
        public async Task MarkFailedAsync(int jobId, string expectedConcurrencyToken, string errorCode,
            CancellationToken cancellationToken = default)
        {
            ErrorCode = errorCode;
            if (Uow is not null)
            {
                var job = await Uow.Repository<StoryGenerationJob>().FirstOrDefaultAsync(x => x.Id == jobId, cancellationToken: cancellationToken);
                if (job?.ConcurrencyToken == expectedConcurrencyToken)
                {
                    job.Status = GenerationJobStatus.Failed;
                    job.ErrorCode = errorCode;
                    job.LeaseExpiresAt = null;
                    Uow.Repository<StoryGenerationJob>().Update(job);
                    await Uow.SaveChangesAsync(cancellationToken);
                }
            }
        }
    }
}
