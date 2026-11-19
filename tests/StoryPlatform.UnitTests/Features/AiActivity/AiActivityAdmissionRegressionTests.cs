using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StoryPlatform.Application.Abstractions.AI;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AiActivity.Services;
using StoryPlatform.Application.Features.AIStoryInput.Models;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.ContentGeneration;
using StoryPlatform.Application.Features.ContentGeneration.DTOs;
using StoryPlatform.Application.Features.ContentGeneration.Interfaces;
using StoryPlatform.Application.Features.ContentGeneration.Quality;
using StoryPlatform.Application.Features.ContentGeneration.Services;
using StoryPlatform.Application.Features.CreatorAssistance;
using StoryPlatform.Application.Features.CreatorAssistance.Interfaces;
using StoryPlatform.Application.Features.CreatorAssistance.Services;
using StoryPlatform.Application.Features.CreatorAuthoring;
using StoryPlatform.Application.Features.CreatorAuthoring.DTOs;
using StoryPlatform.Application.Features.CreatorAuthoring.Interfaces;
using StoryPlatform.Application.Features.CreatorAuthoring.Services;
using StoryPlatform.Application.Features.ExistingStories.Interfaces;
using StoryPlatform.Application.Features.ManualAuthoring.Services;
using StoryPlatform.Application.Features.MediaGeneration;
using StoryPlatform.Application.Features.MediaGeneration.Interfaces;
using StoryPlatform.Application.Features.MediaGeneration.Models;
using StoryPlatform.Application.Features.MediaGeneration.Services;
using StoryPlatform.Application.Features.MediaStorage.Interfaces;
using StoryPlatform.Application.Features.MediaStorage.Models;
using StoryPlatform.Application.Features.Moderation.Services;
using StoryPlatform.Application.Features.StoryReview.DTOs;
using StoryPlatform.Application.Features.StoryReview.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.AiActivity;

public sealed class AiActivityAdmissionRegressionTests
{
    private static void SeedTwoOtherActiveStories(StoryReviewServiceTests.FakeUnitOfWork store, int userId = 1)
    {
        store.Seed(new Story { Id = 91, AuthorUserId = userId, Scope = StoryScope.Creator, Source = StorySource.Ai, Status = StoryStatus.OutlineReview, AiActivityMode = AiActivityMode.FullCreation, AiActivityStartedAt = DateTime.UtcNow.AddMinutes(-2) });
        store.Seed(new Story { Id = 92, AuthorUserId = userId, Scope = StoryScope.Creator, Source = StorySource.Ai, Status = StoryStatus.MediaReview, AiActivityMode = AiActivityMode.FullCreation, AiActivityStartedAt = DateTime.UtcNow.AddMinutes(-1) });
    }

    private static ContentGenerationService CreateContentService(
        StoryReviewServiceTests.FakeUnitOfWork store,
        ILearningBatchService? learningBatches = null) =>
        new(
            store,
            new Mock<IAIStoryGenerationClient>().Object,
            new Mock<IContentQualityEvaluator>().Object,
            new Mock<IContentGenerationJobFailureFinalizer>().Object,
            new Mock<IStableVersionArtifactHandoffService>().Object,
            new ContentGenerationOptions(),
            learningBatches: learningBatches);

    private static string ValidContextSnapshotJson() => JsonSerializer.Serialize(new AIStoryInputContextSnapshot(
        ChildProfileId: null,
        AgeBand: "6-8",
        ReadingLevel: 1,
        VocabularyLevel: "level_1",
        Language: "vi",
        MaximumLength: 500,
        RequiredApprovalMode: "always_manual",
        Interests: [],
        AllowedCategoryCodes: [],
        RestrictedCategoryCodes: [],
        BlockedCategoryCodes: [],
        ReadabilityScoreThreshold: 99m,
        AudienceScope: "Creator",
        CreatorUserId: 1));

    [Fact]
    public async Task ReadabilityRepairRetry_BlockedAtCapacity_And_AllowedWhenStoryAlreadyHoldsSlot()
    {
        var store = new StoryReviewServiceTests.FakeUnitOfWork();
        store.Seed(new UserAccount { Id = 1, Role = UserRole.ContentCreator, Status = AccountStatus.Active });
        SeedTwoOtherActiveStories(store);
        store.Seed(new Story { Id = 1, AuthorUserId = 1, Scope = StoryScope.Creator, Source = StorySource.Ai, Status = StoryStatus.ContentReview, AiActivityMode = AiActivityMode.None });
        store.Seed(new StoryVersion { Id = 10, StoryId = 1, IsCurrent = true, Title = "Tiêu đề", Content = "Một câu văn rất ngắn.", Lesson = "Bài học", OutlineApprovedAt = DateTime.UtcNow });
        store.Seed(new StoryGenerationRequest { Id = 20, StoryId = 1, Status = GenerationInputStatus.InputAccepted, ContextSnapshotJson = ValidContextSnapshotJson() });

        var svc = CreateContentService(store);
        var ex = await Assert.ThrowsAsync<ConflictException>(() => svc.RetryAsync(1, 1, new RetryContentGenerationRequestDto { RetryKey = "repair-1" }));
        Assert.Equal(AiStoryConcurrencyLimits.ErrorCode, ex.ErrorCode);
        Assert.Empty(store.Items<StoryGenerationJob>());

        // When the same story already holds a slot, retry succeeds without consuming an extra slot.
        store.Items<Story>().Single(x => x.Id == 1).AiActivityMode = AiActivityMode.FullCreation;
        var progress = await svc.RetryAsync(1, 1, new RetryContentGenerationRequestDto { RetryKey = "repair-1" });
        Assert.NotNull(progress);
        Assert.Single(store.Items<StoryGenerationJob>(), j => j.Operation == GenerationJobOperation.GenerateContent);
    }

    [Fact]
    public async Task StableHandoffRetry_BlockedAtCapacity_And_ReplaySucceeds()
    {
        var store = new StoryReviewServiceTests.FakeUnitOfWork();
        store.Seed(new UserAccount { Id = 1, Role = UserRole.ContentCreator, Status = AccountStatus.Active });
        SeedTwoOtherActiveStories(store);
        store.Seed(new Story { Id = 1, AuthorUserId = 1, Scope = StoryScope.Creator, Source = StorySource.Ai, Status = StoryStatus.OutlineReview, AiActivityMode = AiActivityMode.None });
        store.Seed(new StoryVersion { Id = 10, StoryId = 1, IsCurrent = true, Title = "Tiêu đề", Content = "Nội dung ổn định.", Lesson = "Bài học", OutlineApprovedAt = DateTime.UtcNow });
        var metadataJson = JsonSerializer.Serialize(new { quality = new { isPassed = true } });
        store.Seed(new StoryGenerationJob
        {
            Id = 30, StoryId = 1, StoryVersionId = 10, Operation = GenerationJobOperation.GenerateContent,
            Status = GenerationJobStatus.Failed, GenerationMetadataJson = metadataJson
        });

        var svc = CreateContentService(store);
        var ex = await Assert.ThrowsAsync<ConflictException>(() => svc.RetryAsync(1, 1, new RetryContentGenerationRequestDto { RetryKey = "handoff-1" }));
        Assert.Equal(AiStoryConcurrencyLimits.ErrorCode, ex.ErrorCode);
        Assert.Equal(GenerationJobStatus.Failed, store.Items<StoryGenerationJob>().Single().Status);

        // If an idempotent receipt already exists, replay succeeds even at capacity.
        store.Seed(new StoryAuthoringOperation
        {
            StoryId = 1, StoryVersionId = 10, ActorUserId = 1, OperationId = "handoff-1",
            Kind = "content-handoff-retry", RequestHash = "content-handoff:10", OperationResponseJson = "{\"Id\":30}"
        });
        var replay = await svc.RetryAsync(1, 1, new RetryContentGenerationRequestDto { RetryKey = "handoff-1" });
        Assert.NotNull(replay);
    }

    [Fact]
    public async Task LearningArtifactsRetry_BlockedAtCapacity()
    {
        var store = new StoryReviewServiceTests.FakeUnitOfWork();
        store.Seed(new UserAccount { Id = 1, Role = UserRole.ContentCreator, Status = AccountStatus.Active });
        SeedTwoOtherActiveStories(store);
        store.Seed(new Story { Id = 1, AuthorUserId = 1, Scope = StoryScope.Creator, Source = StorySource.Ai, Status = StoryStatus.OutlineReview, AiActivityMode = AiActivityMode.None });
        store.Seed(new StoryVersion { Id = 10, StoryId = 1, IsCurrent = true, Title = "Tiêu đề", Content = "Nội dung ổn định.", Lesson = "Bài học" });
        store.Seed(new StoryGenerationJob { Id = 40, StoryId = 1, StoryVersionId = 10, Operation = GenerationJobOperation.GenerateVocabulary, Status = GenerationJobStatus.Failed });

        var learningBatches = new Mock<ILearningBatchService>();
        learningBatches.Setup(x => x.ValidateSourceAsync(40, It.IsAny<Story>(), It.IsAny<StoryVersion>(), 1, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var svc = CreateContentService(store, learningBatches.Object);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => svc.RetryAsync(1, 1, new RetryContentGenerationRequestDto { RetryKey = "learn-retry-1" }));
        Assert.Equal(AiStoryConcurrencyLimits.ErrorCode, ex.ErrorCode);
        Assert.Equal(GenerationJobStatus.Failed, store.Items<StoryGenerationJob>().Single().Status);
    }

    [Fact]
    public async Task ReviewedMediaConfirmation_BlockedAtCapacity_WithoutCreatingJob()
    {
        var store = StoryReviewServiceTests.SeedContentReviewWithAllArtifacts();
        store.Items<UserAccount>().Single().Role = UserRole.ContentCreator;
        var story = store.Items<Story>().Single();
        story.Scope = StoryScope.Creator;
        story.ChildProfileId = null;
        story.AiActivityMode = AiActivityMode.None;
        store.Seed(new StoryGenerationRequest
        {
            Id = 1,
            StoryId = 1,
            SubmittedByUserId = 1,
            Status = GenerationInputStatus.InputAccepted,
            ContextSnapshotJson = JsonSerializer.Serialize(new AIStoryInputContextSnapshot(
                null, "6-8", 2, "level_2", "vi", 1000, "always_manual", [], [], [], [],
                SafetyScoreThreshold: null, AudienceScope: "Creator", CreatorUserId: 1))
        });
        SeedTwoOtherActiveStories(store);

        var version = store.Items<StoryVersion>().Single();
        var svc = new StoryReviewService(store, new Mock<IAIStoryGenerationClient>().Object, new Mock<IProposalCache>().Object);
        var ex = await Assert.ThrowsAsync<ConflictException>(() => svc.ConfirmCreatorContentAsync(1, 1, new ConfirmCreatorContentRequestDto { VersionId = version.Id }));
        Assert.Equal(AiStoryConcurrencyLimits.ErrorCode, ex.ErrorCode);
        Assert.Empty(store.Items<StoryGenerationJob>());
        Assert.Equal(StoryStatus.ContentReview, store.Items<Story>().Single(x => x.Id == 1).Status);
    }

    [Fact]
    public async Task CreatorMediaSelection_BlockedAtCapacity_WithoutInvalidatingConfirmationOrCreatingTask()
    {
        var store = new StoryReviewServiceTests.FakeUnitOfWork();
        store.Seed(new UserAccount { Id = 1, Role = UserRole.ContentCreator, Status = AccountStatus.Active });
        SeedTwoOtherActiveStories(store);
        store.Seed(new Story { Id = 1, AuthorUserId = 1, Scope = StoryScope.Creator, Source = StorySource.Manual, Status = StoryStatus.Ready, AiActivityMode = AiActivityMode.None });
        var confirmedAt = DateTime.UtcNow.AddMinutes(-10);
        store.Seed(new StoryVersion
        {
            Id = 10, StoryId = 1, IsCurrent = true, Title = "Tiêu đề", Content = "Nội dung",
            MediaState = new StoryVersionMediaState { MediaPackageRevision = 1, ConfirmedMediaPackageRevision = 1, MediaConfirmedByUserId = 1, MediaConfirmedAt = confirmedAt }
        });
        store.Seed(new StoryGenerationJob
        {
            Id = 50, StoryId = 1, StoryVersionId = 10, Operation = GenerationJobOperation.GenerateMediaPackage,
            Status = GenerationJobStatus.Completed, MediaContextId = 70, MediaPackageRevision = 1,
            GenerationMetadataJson = "{\"mediaMode\":\"choose\",\"choicePrepared\":true}"
        });
        store.Seed(new StoryScene { Id = 100, StoryVersionId = 10, SceneIndex = 0, SceneText = "Scene" });
        store.Seed(new IllustrationBeat { Id = 200, StorySceneId = 100, VisualFocus = "Focus" });
        store.Seed(new MediaAsset { Id = 300, StoryVersionId = 10, StorySceneId = 100, IllustrationBeatId = 200, Type = MediaType.Illustration, IsCurrent = true, Status = MediaStatus.Ready });

        var mediaSvc = new MediaGenerationService(
            store,
            new Mock<IMediaContextBuilder>().Object,
            new Mock<IStoryBlockParser>().Object,
            new Mock<ISceneSegmentationProvider>().Object,
            new Mock<ISceneCoverageValidator>().Object,
            new SceneSpecificationBuilder(),
            new Mock<IIllustrationBeatPlanner>().Object,
            new Mock<IImageGenerationProvider>().Object,
            new Mock<ITtsProvider>().Object,
            new Mock<IMediaStorage>().Object,
            new Mock<IMediaAlignmentEvaluator>().Object,
            new Mock<IMediaSafetyEvaluator>().Object,
            new Mock<IAudioQualityGate>().Object,
            new Mock<IStorySegmentService>().Object,
            new Mock<IMediaGenerationJobFailureFinalizer>().Object,
            new Mock<IMediaReadinessService>().Object,
            new MediaGenerationOptions(),
            NullLogger<MediaGenerationService>.Instance,
            uploadValidator: new Mock<IMediaUploadValidator>().Object);

        var cmd = new CreatorMediaCommand("select-after-release", 10, 1, ExpectedConfirmed: true);
        var upload = new CreatorMediaUploadCommand(cmd, "Own work", "I have the rights");
        var file = new GeneratedMedia([0x89, 0x50, 0x4E, 0x47], "image/png");
        var ex = await Assert.ThrowsAsync<ConflictException>(() => mediaSvc.SelectCreatorMediaAsync(
            1, 1, 200, image: true, cmd, upload, file));
        Assert.Equal(AiStoryConcurrencyLimits.ErrorCode, ex.ErrorCode);
        Assert.Equal(confirmedAt, store.Items<StoryVersion>().Single().MediaState.MediaConfirmedAt);
        Assert.Equal(1, store.Items<StoryVersion>().Single().MediaState.ConfirmedMediaPackageRevision);
        Assert.Empty(store.Items<MediaAssetGenerationTask>());
    }

    [Fact]
    public async Task ManualAuthoringSave_ResetsMediaReviewToDraft_And_ReleasesSlot()
    {
        var store = new StoryReviewServiceTests.FakeUnitOfWork();
        store.Seed(new UserAccount { Id = 1, Role = UserRole.ContentCreator, Status = AccountStatus.Active });
        store.Seed(new Story
        {
            Id = 1, AuthorUserId = 1, Scope = StoryScope.Creator, Source = StorySource.Manual,
            Status = StoryStatus.MediaReview, AuthoringRevisionToken = "rev1", AiActivityMode = AiActivityMode.FullCreation,
            AiActivityStartedAt = DateTime.UtcNow.AddMinutes(-5)
        });
        store.Seed(new StoryVersion { Id = 10, StoryId = 1, VersionNo = 1, IsCurrent = true, Title = "Cũ", Content = "Nội dung cũ." });

        var manualSvc = new ManualAuthoringService(
            store,
            new CreatorAuthoringOptions(),
            new Mock<IImportPreviewProtector>().Object,
            new CreatorManualAuthoringPolicy(store));

        var saved = await manualSvc.SaveContentAsync(1, 1, new SaveCreatorContentRequest
        {
            OperationId = "save-reset-1",
            BaseVersionId = 10,
            RevisionToken = "rev1",
            Title = "Mới",
            Content = "Nội dung mới được chỉnh sửa."
        });

        Assert.Equal("Draft", saved.Story.Status);
        Assert.Equal(AiActivityMode.None, store.Items<Story>().Single().AiActivityMode);
    }

    [Fact]
    public async Task AssistanceRecover_ExpiredLease_ReleasesTaskOnlySlot()
    {
        var store = new StoryReviewServiceTests.FakeUnitOfWork();
        store.Seed(new UserAccount { Id = 1, Role = UserRole.ContentCreator, Status = AccountStatus.Active });
        store.Seed(new Story
        {
            Id = 1, AuthorUserId = 1, Scope = StoryScope.Creator, Source = StorySource.Manual,
            Status = StoryStatus.Draft, AuthoringRevisionToken = "rev1", AiActivityMode = AiActivityMode.TaskOnly,
            AiActivityStartedAt = DateTime.UtcNow.AddMinutes(-5)
        });
        store.Seed(new StoryAssistanceTask
        {
            Id = 10, StoryId = 1, OwnerUserId = 1, EntryPoint = "ReviewDraft", Status = "Open",
            ActiveOperationId = "op-running", LeaseExpiresAt = DateTime.UtcNow.AddMinutes(-1),
            ContextJson = "{\"operationId\":\"create\",\"revisionToken\":\"rev1\",\"editorSnapshot\":{\"title\":\"T\",\"content\":\"C\"},\"target\":{\"mode\":\"cursor\",\"start\":0,\"length\":0},\"constraints\":{\"keep\":[],\"avoid\":[]}}"
        });
        store.Seed(new StoryAssistanceStep { Id = 20, TaskId = 10, Sequence = 1, OperationId = "op-running", Kind = "Suggestions", Status = "Running" });

        var assistanceSvc = new StoryAssistanceService(
            store,
            new Mock<ICreatorAssistanceAiClient>().Object,
            new CreatorAssistanceOptions(),
            TimeProvider.System);

        var dto = await assistanceSvc.GetAsync(1, 1, 10);
        Assert.Equal("Interrupted", store.Items<StoryAssistanceStep>().Single().Status);
        Assert.Null(store.Items<StoryAssistanceTask>().Single().ActiveOperationId);
        Assert.Equal(AiActivityMode.None, store.Items<Story>().Single().AiActivityMode);
        Assert.Equal(10, dto.TaskId);
    }
}
