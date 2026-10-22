using Moq;
using StoryPlatform.Application.Common;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.Supervision.Interfaces;
using StoryPlatform.Application.Features.CreatorAuthoring;
using StoryPlatform.Application.Features.CreatorAuthoring.Services;
using StoryPlatform.Application.Features.MediaGeneration.Services;
using StoryPlatform.Application.Features.Moderation;
using StoryPlatform.Application.Features.Moderation.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.UnitTests.Features.Moderation;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed class CreatorManualMediaTests
{
    // Independent no-tracking reads and a shared semaphore; not proof of PostgreSQL advisory locks.
    private static (AssistanceStore Store, CreatorManualMediaService Service) ConcurrentSetup(Func<GrayZoneScan, Task<ModelModerationResult>> evaluate)
    {
        var store = new AssistanceStore();
        store.Items<UserAccount>()[0].Role = UserRole.ContentCreator;
        store.Items<Story>()[0].OutputMode = StoryOutputMode.ReadingMediaOnly;
        store.Items<StoryVersion>().Add(new() { Id = 10, StoryId = 1, IsCurrent = true, Title = "Na giúp bạn",
            Content = "Na giúp bạn sửa đồ chơi.", SourceDeclaration = "Tự viết", RightsDeclaration = "Tôi có quyền" });
        var provider = new Mock<IModelModerationProvider>(); provider.SetupGet(x => x.Name).Returns("test-only");
        provider.Setup(x => x.EvaluateAsync(It.IsAny<GrayZoneScan>(), It.IsAny<CancellationToken>()))
            .Returns((GrayZoneScan scan, CancellationToken _) => evaluate(scan));
        var evaluator = new GrayZoneEvaluator(new() { Provider = "test-only", WarningThreshold = 50, BlockThreshold = 100, MaximumRiskScore = 100 },
            [provider.Object], RubricTestData.Source());
        var audit = new Mock<IAuditLogWriter>();
        var checks = new GrayZoneContentCheckService(store.Uow.Object, evaluator,
            new GrayZoneWorkflowService(store.Uow.Object, audit.Object), new Mock<ISupervisionAccessGuard>().Object);
        return (store, new CreatorManualMediaService(store.Uow.Object, checks, evaluator, new MediaContextBuilder(), audit.Object, new()));
    }

    [Fact]
    public async Task TwoOverlappingConfirmsQueueExactlyOneJobAndOneContext()
    {
        var bothEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        var (store, service) = ConcurrentSetup(async scan =>
        {
            if (Interlocked.Increment(ref calls) == 2) bothEntered.TrySetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(10));
            return new(0, "test", []) { RubricVersion = scan.RubricVersion };
        });
        var first = service.ConfirmAsync(1, 1, new(10, "revision"));
        var second = service.ConfirmAsync(1, 1, new(10, "revision"));
        await bothEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        release.SetResult();
        var results = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(results[0].JobId, results[1].JobId);
        Assert.Single(store.Items<StoryGenerationJob>());
        Assert.Single(store.Items<MediaContext>());
    }

    [Fact]
    public async Task SaveWinsWhileSafetyRunsConfirmCannotQueueOldVersion()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (store, service) = ConcurrentSetup(async scan =>
        {
            entered.SetResult(); await release.Task.WaitAsync(TimeSpan.FromSeconds(10));
            return new(0, "test", []) { RubricVersion = scan.RubricVersion };
        });
        var confirmation = service.ConfirmAsync(1, 1, new(10, "revision"));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var saved = await store.Authoring.SaveContentAsync(1, 1, new() { OperationId = "v2", BaseVersionId = 10,
            RevisionToken = "revision", Title = "Bản mới", Content = "Na và Nam cùng sửa xe." });
        release.SetResult();
        await Assert.ThrowsAsync<ConflictException>(() => confirmation.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(saved.Version.VersionId, store.Items<StoryVersion>().Single(x => x.IsCurrent).Id);
        Assert.Empty(store.Items<StoryGenerationJob>());
    }

    internal static (StoryReviewServiceTests.FakeUnitOfWork Store, CreatorManualMediaService Service, Mock<IModelModerationProvider> Provider) Setup(decimal risk = 0)
    {
        var store = new StoryReviewServiceTests.FakeUnitOfWork();
        store.Seed(new UserAccount { Id = 1, Role = UserRole.ContentCreator, Status = AccountStatus.Active });
        store.Seed(new Story { Id = 1, Scope = StoryScope.Creator, Source = StorySource.Manual, AuthorUserId = 1,
            Status = StoryStatus.Draft, OutputMode = StoryOutputMode.ReadingMediaOnly, AuthoringRevisionToken = "rev1" });
        store.Seed(new StoryVersion { Id = 10, StoryId = 1, IsCurrent = true, VersionNo = 1, Title = "Na và bạn",
            Content = "Na giúp bạn sửa đồ chơi.", SourceDeclaration = "Tự viết", RightsDeclaration = "Tôi có quyền" });
        var provider = new Mock<IModelModerationProvider>();
        provider.SetupGet(x => x.Name).Returns("test-only");
        provider.Setup(x => x.EvaluateAsync(It.IsAny<GrayZoneScan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GrayZoneScan scan, CancellationToken _) => new ModelModerationResult(risk, "test", risk == 0 ? [] :
                [new("paragraph", "paragraph:0", "GZ02", "Cần đọc ngữ cảnh")]) { RubricVersion = scan.RubricVersion });
        var evaluator = new GrayZoneEvaluator(new() { Provider = "test-only", WarningThreshold = 50, BlockThreshold = 100, MaximumRiskScore = 100 },
            [provider.Object], RubricTestData.Source());
        var audit = new Mock<IAuditLogWriter>();
        var checks = new GrayZoneContentCheckService(store, evaluator, new GrayZoneWorkflowService(store, audit.Object), new Mock<ISupervisionAccessGuard>().Object);
        return (store, new CreatorManualMediaService(store, checks, evaluator, new MediaContextBuilder(), audit.Object, new()), provider);
    }

    [Fact]
    public async Task ConfirmQueuesOnlyPhase5AndReplayDoesNotCallProviderOrCreateAnotherContext()
    {
        var (store, service, provider) = Setup();
        var first = await service.ConfirmAsync(1, 1, new(10, "rev1"));
        var again = await service.ConfirmAsync(1, 1, new(10, "rev1"));
        Assert.Equal(first, again);
        var job = Assert.Single(store.Items<StoryGenerationJob>());
        Assert.Equal(GenerationJobOperation.GenerateMediaPackage, job.Operation);
        Assert.Equal(10, job.StoryVersionId);
        Assert.Equal(10, job.BaseStoryVersionId);
        Assert.Null(job.GenerationRequestId);
        Assert.Single(store.Items<MediaContext>());
        Assert.Empty(store.Items<StoryGenerationRequest>());
        var version = Assert.Single(store.Items<StoryVersion>());
        Assert.Null(version.OutlineOpening);
        Assert.Equal("Na giúp bạn sửa đồ chơi.", version.Content);
        Assert.Equal(StoryStatus.MediaProcessing, store.Items<Story>().Single().Status);
        Assert.False(store.Items<Story>().Single().IsPublished);
        provider.Verify(x => x.EvaluateAsync(It.IsAny<GrayZoneScan>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(50, true)]
    [InlineData(100, false)]
    public async Task GrayZoneKeepsFlagsButHardBlockDoesNotQueue(decimal risk, bool queues)
    {
        var (store, service, _) = Setup(risk);
        if (queues) await service.ConfirmAsync(1, 1, new(10, "rev1"));
        else await Assert.ThrowsAsync<ConflictException>(() => service.ConfirmAsync(1, 1, new(10, "rev1")));
        Assert.Equal(queues ? 1 : 0, store.Items<StoryGenerationJob>().Count);
        Assert.Empty(store.Items<ModerationItem>());
        if (queues) Assert.Single(PublicReviewState.ParseVersionFlags(store.Items<StoryVersion>().Single().GrayZoneFlags));
    }

    [Theory]
    [InlineData("stale")]
    [InlineData("")]
    public async Task StaleRevisionDoesNotCallProvider(string revision)
    {
        var (store, service, provider) = Setup();
        await Assert.ThrowsAsync<ConflictException>(() => service.ConfirmAsync(1, 1, new(10, revision)));
        provider.Verify(x => x.EvaluateAsync(It.IsAny<GrayZoneScan>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(store.Items<StoryGenerationJob>());
    }

    [Fact]
    public async Task ProviderFailureKeepsDraftAndDoesNotCreateJobOrContext()
    {
        var (store, service, provider) = Setup();
        provider.Setup(x => x.EvaluateAsync(It.IsAny<GrayZoneScan>(), It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException());
        await Assert.ThrowsAsync<TimeoutException>(() => service.ConfirmAsync(1, 1, new(10, "rev1")));
        Assert.Equal(StoryStatus.Draft, store.Items<Story>().Single().Status);
        Assert.Empty(store.Items<StoryGenerationJob>());
        Assert.Empty(store.Items<MediaContext>());
    }

    [Fact]
    public async Task ContentChangesWhileProviderRunsCannotQueueOldVersion()
    {
        var (store, service, provider) = Setup();
        provider.Setup(x => x.EvaluateAsync(It.IsAny<GrayZoneScan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GrayZoneScan scan, CancellationToken _) =>
            {
                store.Items<Story>().Single().AuthoringRevisionToken = "rev2";
                return new ModelModerationResult(0, "test", []) { RubricVersion = scan.RubricVersion };
            });
        await Assert.ThrowsAsync<ConflictException>(() => service.ConfirmAsync(1, 1, new(10, "rev1")));
        Assert.Empty(store.Items<StoryGenerationJob>());
        Assert.Empty(store.Items<MediaContext>());
    }

    [Theory]
    [InlineData("title")]
    [InlineData("content")]
    [InlineData("rights")]
    [InlineData("source")]
    public async Task IncompleteSavedContentCannotStartMedia(string field)
    {
        var (store, service, provider) = Setup();
        var v = store.Items<StoryVersion>().Single();
        if (field == "title") v.Title = "";
        if (field == "content") v.Content = "";
        if (field == "rights") v.RightsDeclaration = "";
        if (field == "source") v.SourceDeclaration = "";
        await Assert.ThrowsAsync<BadRequestException>(() => service.ConfirmAsync(1, 1, new(10, "rev1")));
        provider.Verify(x => x.EvaluateAsync(It.IsAny<GrayZoneScan>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(store.Items<StoryGenerationJob>());
    }

    [Fact]
    public async Task ManualCanUseMediaButCannotEnterGenerationEndpoints()
    {
        var (store, service, _) = Setup();
        await Assert.ThrowsAsync<NotFoundException>(() => service.ConfirmAsync(2, 1, new(10, "rev1")));
        var story = store.Items<Story>().Single();
        Assert.Throws<NotFoundException>(() => GenerationStoryAccess.EnsureSupported(story));
        MediaStoryAccess.EnsureSupported(story);
        store.Items<UserAccount>().Single().Status = AccountStatus.Suspended;
        await Assert.ThrowsAsync<ForbiddenException>(() => service.ConfirmAsync(1, 1, new(10, "rev1")));
    }
}
