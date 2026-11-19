using System.Linq.Expressions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StoryPlatform.Api.Middleware;
using StoryPlatform.Application.Abstractions.AI;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AiActivity.Services;
using StoryPlatform.Application.Features.AIStoryInput.DTOs;
using StoryPlatform.Application.Features.AIStoryInput.Guardrails;
using StoryPlatform.Application.Features.AIStoryInput.Services;
using StoryPlatform.Application.Features.Moderation;
using StoryPlatform.Application.Features.Moderation.Services;
using StoryPlatform.Application.Features.TokenQuota.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.AiActivity;

public sealed class AccountAiActivityServiceTests
{
    [Fact]
    public async Task TwoDistinctStoriesAccepted_ThirdRejectedWithoutSideEffects()
    {
        var uow = new TestUnitOfWork();
        uow.Seed(new UserAccount { Id = 10, Role = UserRole.ContentCreator, Status = AccountStatus.Active });
        var quota = new Mock<ITokenQuotaService>(MockBehavior.Strict);
        quota.Setup(q => q.EnsureCreatorWithinQuotaAsync(10, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        quota.Setup(q => q.IncrementCreatorUsageAsync(10, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var service = new AIStoryInputService(uow, new RuleBasedInputGuardrail(), quota.Object);

        var r1 = await service.SubmitCreatorAsync(10, CreatorRequest("idem-key-1", "Chủ đề 1"));
        var r2 = await service.SubmitCreatorAsync(10, CreatorRequest("idem-key-2", "Chủ đề 2"));
        Assert.Equal("input_accepted", r1.InputStatus);
        Assert.Equal("input_accepted", r2.InputStatus);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.SubmitCreatorAsync(10, CreatorRequest("idem-key-3", "Chủ đề 3")));
        Assert.Equal(AiStoryConcurrencyLimits.ErrorCode, ex.ErrorCode);
        Assert.Equal(AiStoryConcurrencyLimits.ErrorMessage, ex.Message);

        Assert.Equal(2, uow.Items<Story>().Count);
        Assert.Equal(2, uow.Items<StoryGenerationJob>().Count);
        quota.Verify(q => q.IncrementCreatorUsageAsync(10, It.IsAny<CancellationToken>()), Times.Exactly(2));

        // Idempotent replay of an admitted story succeeds even at capacity.
        var replay = await service.SubmitCreatorAsync(10, CreatorRequest("idem-key-1", "Chủ đề 1"));
        Assert.Equal(r1.RequestId, replay.RequestId);
        quota.Verify(q => q.IncrementCreatorUsageAsync(10, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task AccountIsolation_And_CapacityProjection()
    {
        var uow = new TestUnitOfWork();
        uow.Seed(
            new Story { Id = 1, AuthorUserId = 10, Title = "S1", Status = StoryStatus.OutlineReview, AiActivityMode = AiActivityMode.FullCreation, AiActivityStartedAt = DateTime.UtcNow.AddMinutes(-5) },
            new Story { Id = 2, AuthorUserId = 10, Title = "S2", Status = StoryStatus.MediaReview, AiActivityMode = AiActivityMode.FullCreation, AiActivityStartedAt = DateTime.UtcNow.AddMinutes(-2) },
            new Story { Id = 3, AuthorUserId = 20, Title = "Other", Status = StoryStatus.OutlineReview, AiActivityMode = AiActivityMode.FullCreation, AiActivityStartedAt = DateTime.UtcNow });

        var svc = new AccountAiActivityService(uow);
        var cap10 = await svc.GetCapacityAsync(10);
        Assert.Equal(2, cap10.Limit);
        Assert.Equal(2, cap10.ActiveCount);
        Assert.False(cap10.CanStartNewStory);
        Assert.Equal([1, 2], cap10.ActiveStories.Select(s => s.StoryId).ToArray());

        var cap20 = await svc.GetCapacityAsync(20);
        Assert.Equal(1, cap20.ActiveCount);
        Assert.True(cap20.CanStartNewStory);
        await svc.EnsureCanAdmitNewStoryAsync(20);
    }

    [Fact]
    public async Task TaskOnlyPromotion_KeepsOriginalStartTime_And_DoesNotConsumeExtraSlot()
    {
        var uow = new TestUnitOfWork();
        var originalStart = new DateTime(2026, 11, 18, 1, 0, 0, DateTimeKind.Utc);
        uow.Seed(
            new Story { Id = 1, AuthorUserId = 10, Status = StoryStatus.Draft, AiActivityMode = AiActivityMode.TaskOnly, AiActivityStartedAt = originalStart },
            new Story { Id = 2, AuthorUserId = 10, Status = StoryStatus.ContentReview, AiActivityMode = AiActivityMode.FullCreation, AiActivityStartedAt = originalStart.AddMinutes(1) });

        var svc = new AccountAiActivityService(uow);
        await svc.AdmitStoryAsync(10, 1, AiActivityMode.FullCreation);

        var s1 = uow.Items<Story>().Single(x => x.Id == 1);
        Assert.Equal(AiActivityMode.FullCreation, s1.AiActivityMode);
        Assert.Equal(originalStart, s1.AiActivityStartedAt);
    }

    [Theory]
    [InlineData(StoryStatus.OutlineReview)]
    [InlineData(StoryStatus.ContentReview)]
    [InlineData(StoryStatus.Approved)]
    [InlineData(StoryStatus.MediaReview)]
    public async Task FullCreation_HeldThroughAllReviewStates(StoryStatus status)
    {
        var uow = new TestUnitOfWork();
        uow.Seed(new Story { Id = 1, AuthorUserId = 10, Status = status, AiActivityMode = AiActivityMode.FullCreation, AiActivityStartedAt = DateTime.UtcNow });

        var svc = new AccountAiActivityService(uow);
        await svc.ReconcileStoryAiActivityAsync(10, 1);

        Assert.Equal(AiActivityMode.FullCreation, uow.Items<Story>().Single().AiActivityMode);
    }

    [Fact]
    public async Task FullCreation_HeldOnReadyUntilMediaConfirmed_ThenReleased_AndRetryReacquires()
    {
        var uow = new TestUnitOfWork();
        uow.Seed(
            new Story { Id = 1, AuthorUserId = 10, Status = StoryStatus.Ready, AiActivityMode = AiActivityMode.FullCreation, AiActivityStartedAt = DateTime.UtcNow });
        uow.Seed(
            new StoryVersion
            {
                Id = 101,
                StoryId = 1,
                IsCurrent = true,
                MediaState = new StoryVersionMediaState { MediaPackageRevision = 1, ConfirmedMediaPackageRevision = 0 }
            });

        var svc = new AccountAiActivityService(uow);
        await svc.ReconcileStoryAiActivityAsync(10, 1);
        Assert.Equal(AiActivityMode.FullCreation, uow.Items<Story>().Single().AiActivityMode);

        var version = uow.Items<StoryVersion>().Single();
        version.MediaState.ConfirmedMediaPackageRevision = 1;
        version.MediaState.MediaConfirmedByUserId = 10;
        version.MediaState.MediaConfirmedAt = DateTime.UtcNow;
        await svc.ReconcileStoryAiActivityAsync(10, 1);
        Assert.Equal(AiActivityMode.None, uow.Items<Story>().Single().AiActivityMode);

        // Retry / regeneration after release must reacquire.
        await svc.AdmitStoryAsync(10, 1, AiActivityMode.FullCreation);
        Assert.Equal(AiActivityMode.FullCreation, uow.Items<Story>().Single().AiActivityMode);
    }

    [Fact]
    public async Task MultipleParentPackages_HeldUntilAllConfirmed()
    {
        var uow = new TestUnitOfWork();
        uow.Seed(
            new Story { Id = 1, AuthorUserId = 10, Scope = StoryScope.Parent, Source = StorySource.Manual, ChildProfileId = null, CreateOperationId = "op-1", Status = StoryStatus.Draft, AiActivityMode = AiActivityMode.FullCreation, AiActivityStartedAt = DateTime.UtcNow });
        uow.Seed(
            new StoryVersion { Id = 10, StoryId = 1, IsCurrent = true });
        uow.Seed(
            new ParentStoryPackage { Id = 100, StoryId = 1, StoryVersionId = 10, ChildProfileId = 1, CreatedByUserId = 10, Status = "Confirmed" },
            new ParentStoryPackage { Id = 101, StoryId = 1, StoryVersionId = 10, ChildProfileId = 2, CreatedByUserId = 10, Status = "Review" });

        var svc = new AccountAiActivityService(uow);
        await svc.ReconcileStoryAiActivityAsync(10, 1);
        Assert.Equal(AiActivityMode.FullCreation, uow.Items<Story>().Single().AiActivityMode);

        uow.Items<ParentStoryPackage>().Single(p => p.Id == 101).Status = "Confirmed";
        await svc.ReconcileStoryAiActivityAsync(10, 1);
        Assert.Equal(AiActivityMode.None, uow.Items<Story>().Single().AiActivityMode);
    }

    [Fact]
    public async Task InlineParallelTaskOnly_FirstCompletionDoesNotReleaseWhileSecondRunning()
    {
        var uow = new TestUnitOfWork();
        uow.Seed(
            new Story { Id = 1, AuthorUserId = 10, Scope = StoryScope.Creator, Status = StoryStatus.Draft, AiActivityMode = AiActivityMode.TaskOnly, AiActivityStartedAt = DateTime.UtcNow });
        uow.Seed(
            new StoryGenerationJob { Id = 1, StoryId = 1, Operation = GenerationJobOperation.GrayZoneCheck, Status = GenerationJobStatus.Completed },
            new StoryGenerationJob { Id = 2, StoryId = 1, Operation = GenerationJobOperation.GrayZoneCheck, Status = GenerationJobStatus.Processing });

        var svc = new AccountAiActivityService(uow);
        await svc.ReconcileStoryAiActivityAsync(10, 1);
        Assert.Equal(AiActivityMode.TaskOnly, uow.Items<Story>().Single().AiActivityMode);

        uow.Items<StoryGenerationJob>().Single(j => j.Id == 2).Status = GenerationJobStatus.Completed;
        await svc.ReconcileStoryAiActivityAsync(10, 1);
        Assert.Equal(AiActivityMode.None, uow.Items<Story>().Single().AiActivityMode);
    }

    [Fact]
    public async Task ExceptionHandlingMiddleware_Returns409WithStableConcurrencyCode()
    {
        var middleware = new ExceptionHandlingMiddleware(
            _ => throw new ConflictException(AiStoryConcurrencyLimits.ErrorMessage, AiStoryConcurrencyLimits.ErrorCode),
            NullLogger<ExceptionHandlingMiddleware>.Instance);

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        var json = await new StreamReader(context.Response.Body).ReadToEndAsync();
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.Contains(AiStoryConcurrencyLimits.ErrorCode, json);
        Assert.Equal(AiStoryConcurrencyLimits.ErrorMessage, doc.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task FullCreation_ReadyAndConfirmedWithAcceptedRequest_ReleasesSlot()
    {
        var uow = new TestUnitOfWork();
        uow.Seed(new Story { Id = 1, AuthorUserId = 10, Source = StorySource.Ai, Status = StoryStatus.Ready, AiActivityMode = AiActivityMode.FullCreation, AiActivityStartedAt = DateTime.UtcNow });
        uow.Seed(new StoryVersion
        {
            Id = 101,
            StoryId = 1,
            IsCurrent = true,
            MediaState = new StoryVersionMediaState { MediaPackageRevision = 1, ConfirmedMediaPackageRevision = 1, MediaConfirmedByUserId = 10, MediaConfirmedAt = DateTime.UtcNow }
        });
        uow.Seed(new StoryGenerationRequest { Id = 201, StoryId = 1, Status = GenerationInputStatus.InputAccepted });

        var svc = new AccountAiActivityService(uow);
        await svc.ReconcileStoryAiActivityAsync(10, 1);

        Assert.Equal(AiActivityMode.None, uow.Items<Story>().Single().AiActivityMode);
    }

    [Fact]
    public async Task ParentOneChildManual_WithoutPackages_HeldThroughMediaReviewUntilMediaConfirmed()
    {
        var uow = new TestUnitOfWork();
        uow.Seed(new Story { Id = 1, AuthorUserId = 10, Scope = StoryScope.Parent, Source = StorySource.Manual, ChildProfileId = 3, Status = StoryStatus.MediaReview, AiActivityMode = AiActivityMode.FullCreation, AiActivityStartedAt = DateTime.UtcNow });
        uow.Seed(new StoryVersion
        {
            Id = 101,
            StoryId = 1,
            IsCurrent = true,
            MediaState = new StoryVersionMediaState { MediaPackageRevision = 1, ConfirmedMediaPackageRevision = 0 }
        });

        var svc = new AccountAiActivityService(uow);
        await svc.ReconcileStoryAiActivityAsync(10, 1);
        Assert.Equal(AiActivityMode.FullCreation, uow.Items<Story>().Single().AiActivityMode);

        uow.Items<Story>().Single().Status = StoryStatus.Ready;
        await svc.ReconcileStoryAiActivityAsync(10, 1);
        Assert.Equal(AiActivityMode.FullCreation, uow.Items<Story>().Single().AiActivityMode);

        var version = uow.Items<StoryVersion>().Single();
        version.MediaState.ConfirmedMediaPackageRevision = 1;
        version.MediaState.MediaConfirmedByUserId = 10;
        version.MediaState.MediaConfirmedAt = DateTime.UtcNow;
        await svc.ReconcileStoryAiActivityAsync(10, 1);
        Assert.Equal(AiActivityMode.None, uow.Items<Story>().Single().AiActivityMode);
    }

    [Fact]
    public async Task MultipleParentPackages_CheckingHoldsSlot_And_BlockedReleases()
    {
        var uow = new TestUnitOfWork();
        uow.Seed(new Story { Id = 1, AuthorUserId = 10, Scope = StoryScope.Parent, Source = StorySource.Manual, ChildProfileId = null, CreateOperationId = "op-1", Status = StoryStatus.Draft, AiActivityMode = AiActivityMode.FullCreation, AiActivityStartedAt = DateTime.UtcNow });
        uow.Seed(new StoryVersion { Id = 10, StoryId = 1, IsCurrent = true });
        uow.Seed(
            new ParentStoryPackage { Id = 100, StoryId = 1, StoryVersionId = 10, ChildProfileId = 1, CreatedByUserId = 10, Status = "Confirmed" },
            new ParentStoryPackage { Id = 101, StoryId = 1, StoryVersionId = 10, ChildProfileId = 2, CreatedByUserId = 10, Status = "Checking" });

        var svc = new AccountAiActivityService(uow);
        await svc.ReconcileStoryAiActivityAsync(10, 1);
        Assert.Equal(AiActivityMode.FullCreation, uow.Items<Story>().Single().AiActivityMode);

        uow.Items<ParentStoryPackage>().Single(p => p.Id == 101).Status = "Blocked";
        await svc.ReconcileStoryAiActivityAsync(10, 1);
        Assert.Equal(AiActivityMode.None, uow.Items<Story>().Single().AiActivityMode);
    }

    [Fact]
    public async Task StaleCompletion_DoesNotRelease_WhenNewActiveJobExists()
    {
        var uow = new TestUnitOfWork();
        uow.Seed(new Story { Id = 1, AuthorUserId = 10, Source = StorySource.Manual, Status = StoryStatus.Draft, AiActivityMode = AiActivityMode.FullCreation, AiActivityStartedAt = DateTime.UtcNow });
        uow.Seed(new StoryGenerationJob { Id = 2, StoryId = 1, Operation = GenerationJobOperation.GenerateMediaPackage, Status = GenerationJobStatus.Pending });

        var svc = new AccountAiActivityService(uow);
        await svc.ReconcileStoryAiActivityAsync(10, 1);

        Assert.Equal(AiActivityMode.FullCreation, uow.Items<Story>().Single().AiActivityMode);
    }

    [Fact]
    public async Task GrayZoneCheck_OverlappingChecks_ProtectSlotUntilLastCompletes()
    {
        var uow = new TestUnitOfWork();
        uow.Seed(new UserAccount { Id = 10, Role = UserRole.ContentCreator, Status = AccountStatus.Active });
        uow.Seed(new Story { Id = 1, AuthorUserId = 10, Scope = StoryScope.Creator, Source = StorySource.Manual, Status = StoryStatus.Draft });
        uow.Seed(new StoryVersion { Id = 10, StoryId = 1, IsCurrent = true, Title = "Tiêu đề", Content = "Nội dung an toàn." });

        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callCount = 0;
        var evaluator = new Mock<IGrayZoneEvaluator>();
        evaluator.SetupGet(e => e.IsConfigured).Returns(true);
        evaluator.Setup(e => e.CanReuseAsync(It.IsAny<GrayZoneAssessment>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        evaluator.Setup(e => e.EvaluateAsync(It.IsAny<GrayZoneScan>(), It.IsAny<CancellationToken>()))
            .Returns(async (GrayZoneScan scan, CancellationToken _) =>
            {
                if (Interlocked.Increment(ref callCount) == 1)
                {
                    firstStarted.SetResult();
                    await releaseFirst.Task;
                }
                return new GrayZoneAssessment(ModerationZone.Safe, GrayZoneEvaluator.Fingerprint(scan), scan.Phase, "p1", "test", "test", 0, []);
            });

        var audit = new Mock<StoryPlatform.Application.Features.AuditLogs.Interfaces.IAuditLogWriter>();
        var service = new GrayZoneContentCheckService(uow, evaluator.Object, new GrayZoneWorkflowService(uow, audit.Object),
            new Mock<StoryPlatform.Application.Features.ChildProfiles.Profiles.Interfaces.IChildProfileAccessGuard>().Object);

        var firstTask = service.CheckAsync(10, 1, 10, CancellationToken.None);
        await firstStarted.Task;
        Assert.Equal(AiActivityMode.TaskOnly, uow.Items<Story>().Single().AiActivityMode);

        await service.CheckAsync(10, 1, 10, CancellationToken.None);
        Assert.Equal(AiActivityMode.TaskOnly, uow.Items<Story>().Single().AiActivityMode);

        releaseFirst.SetResult();
        await firstTask;
        Assert.Equal(AiActivityMode.None, uow.Items<Story>().Single().AiActivityMode);
        Assert.Equal(2, uow.Items<StoryGenerationJob>().Count(j => j.Operation == GenerationJobOperation.GrayZoneCheck && j.Status == GenerationJobStatus.Completed));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GrayZoneCheck_CleanupFailure_PreservesPrimaryErrorOrRethrowsOnSuccess_AndKeepsDurableWork(bool providerSucceeds)
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new CreatorManualMediaPersistenceTests.Db(
            new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<StoryPlatform.Infrastructure.Persistence.ApplicationDbContext>()
                .UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        db.Users.Add(new UserAccount { Id = 10, Role = UserRole.ContentCreator, Status = AccountStatus.Active });
        db.Stories.Add(new Story { Id = 1, AuthorUserId = 10, Scope = StoryScope.Creator, Source = StorySource.Manual, Status = StoryStatus.Draft });
        db.StoryVersions.Add(new StoryVersion { Id = 10, StoryId = 1, IsCurrent = true, Title = "Tiêu đề", Content = "Nội dung an toàn." });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await using var real = new StoryPlatform.Infrastructure.Persistence.Repositories.UnitOfWork(db);
        var commitCalls = 0;
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.Repository<UserAccount>()).Returns(real.Repository<UserAccount>());
        uow.Setup(x => x.Repository<Story>()).Returns(real.Repository<Story>());
        uow.Setup(x => x.Repository<StoryVersion>()).Returns(real.Repository<StoryVersion>());
        uow.Setup(x => x.Repository<StoryGenerationJob>()).Returns(real.Repository<StoryGenerationJob>());
        uow.Setup(x => x.Repository<StoryGenerationRequest>()).Returns(real.Repository<StoryGenerationRequest>());
        uow.Setup(x => x.Repository<StoryAssistanceTask>()).Returns(real.Repository<StoryAssistanceTask>());
        uow.Setup(x => x.Repository<LearningArtifactGenerationBatch>()).Returns(real.Repository<LearningArtifactGenerationBatch>());
        uow.Setup(x => x.Repository<ParentStoryPackage>()).Returns(real.Repository<ParentStoryPackage>());
        uow.Setup(x => x.Repository<ModerationItem>()).Returns(real.Repository<ModerationItem>());
        uow.Setup(x => x.Repository<ContentCategory>()).Returns(real.Repository<ContentCategory>());
        uow.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns((CancellationToken ct) => real.BeginTransactionAsync(ct));
        uow.Setup(x => x.RollbackTransactionAsync(It.IsAny<CancellationToken>())).Returns((CancellationToken ct) => real.RollbackTransactionAsync(ct));
        uow.Setup(x => x.AcquireAccountAiActivityLockAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        uow.Setup(x => x.AcquireTransactionLockAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns((CancellationToken ct) => real.SaveChangesAsync(ct));
        var cleanupError = new InvalidOperationException("cleanup-commit-failed");
        uow.Setup(x => x.CommitTransactionAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken ct) => ++commitCalls == 2 ? Task.FromException(cleanupError) : real.CommitTransactionAsync(ct));

        var providerError = new TimeoutException("provider-timeout");
        var evaluator = new Mock<IGrayZoneEvaluator>();
        evaluator.SetupGet(e => e.IsConfigured).Returns(true);
        evaluator.Setup(e => e.EvaluateAsync(It.IsAny<GrayZoneScan>(), It.IsAny<CancellationToken>()))
            .Returns<GrayZoneScan, CancellationToken>((scan, _) => providerSucceeds
                ? Task.FromResult(new GrayZoneAssessment(ModerationZone.Safe, GrayZoneEvaluator.Fingerprint(scan), scan.Phase, "p1", "test", "test", 0, []))
                : Task.FromException<GrayZoneAssessment>(providerError));

        var audit = new Mock<StoryPlatform.Application.Features.AuditLogs.Interfaces.IAuditLogWriter>();
        var service = new GrayZoneContentCheckService(uow.Object, evaluator.Object, new GrayZoneWorkflowService(uow.Object, audit.Object),
            new Mock<StoryPlatform.Application.Features.ChildProfiles.Profiles.Interfaces.IChildProfileAccessGuard>().Object);

        var thrown = await Record.ExceptionAsync(() => service.CheckAsync(10, 1, 10, CancellationToken.None));
        Assert.Same(providerSucceeds ? (Exception)cleanupError : providerError, thrown);
        db.ChangeTracker.Clear();
        Assert.Null((await db.StoryVersions.FindAsync(10))!.Moderation.ModerationZone);
        Assert.Equal(AiActivityMode.TaskOnly, (await db.Stories.FindAsync(1))!.AiActivityMode);
        var job = Assert.Single(db.StoryGenerationJobs.Where(j => j.Operation == GenerationJobOperation.GrayZoneCheck).ToList());
        Assert.Equal(GenerationJobStatus.Processing, job.Status);
    }

    private static SubmitCreatorAIStoryInputRequestDto CreatorRequest(string key, string topic) => new()
    {
        AgeBand = "6-8",
        ReadingLevel = 2,
        Topic = topic,
        CharacterMode = "ai_suggested",
        SettingMode = "ai_suggested",
        Lesson = "Bài học tử tế",
        VocabularyLevel = "level_1",
        Language = "vi",
        TargetLength = 300,
        OutputMode = "reading_media_only",
        IdempotencyKey = key
    };

    private sealed class TestUnitOfWork : IUnitOfWork
    {
        private readonly Dictionary<Type, object> _repos = new();
        private int _nextId = 1000;

        public void Seed<T>(params T[] entities) where T : class
        {
            var repo = (TestRepo<T>)Repository<T>();
            foreach (var e in entities)
            {
                AssignIdIfZero(e);
                repo.Items.Add(e);
            }
        }

        public List<T> Items<T>() where T : class => ((TestRepo<T>)Repository<T>()).Items;

        private void AssignIdIfZero<T>(T entity) where T : class
        {
            if (entity is BaseEntity be && be.Id == 0)
            {
                be.Id = ++_nextId;
            }
        }

        public IGenericRepository<T> Repository<T>() where T : class =>
            (IGenericRepository<T>)GetOrAdd(typeof(T), () => new TestRepo<T>(AssignIdIfZero));

        private object GetOrAdd(Type t, Func<object> factory)
        {
            if (!_repos.TryGetValue(t, out var repo))
            {
                repo = factory();
                _repos[t] = repo;
            }
            return repo;
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
        public Task BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task AcquireTransactionLockAsync(int resourceId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task AcquireAccountAiActivityLockAsync(int accountUserId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommitTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class TestRepo<T>(Action<T> assignId) : IGenericRepository<T> where T : class
    {
        public List<T> Items { get; } = new();
        public IQueryable<T> Query() => Items.AsQueryable();
        public Task<T?> GetByIdAsync(object id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.FirstOrDefault(x => x is BaseEntity be && Equals(be.Id, id)));
        public Task<IReadOnlyList<T>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<T>>(Items.ToList());
        public Task<IReadOnlyList<T>> FindAsync(Expression<Func<T, bool>> predicate, string? includeProperties = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<T>>(Items.Where(predicate.Compile()).ToList());
        public Task<T?> FirstOrDefaultAsync(Expression<Func<T, bool>> predicate, string? includeProperties = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.FirstOrDefault(predicate.Compile()));
        public Task<(IReadOnlyList<T> Items, int TotalCount)> GetPagedAsync(
            int pageIndex,
            int pageSize,
            Expression<Func<T, bool>>? filter = null,
            Func<IQueryable<T>, IOrderedQueryable<T>>? orderBy = null,
            string? includeProperties = null,
            CancellationToken cancellationToken = default)
        {
            var query = filter is null ? Items.AsQueryable() : Items.Where(filter.Compile()).AsQueryable();
            var total = query.Count();
            if (orderBy is not null) query = orderBy(query);
            var paged = query.Skip(Math.Max(0, (pageIndex - 1) * pageSize)).Take(pageSize).ToList();
            return Task.FromResult(((IReadOnlyList<T>)paged, total));
        }
        public Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.Any(predicate.Compile()));
        public Task<int> CountAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(predicate is null ? Items.Count : Items.Count(predicate.Compile()));
        public Task<T> AddAsync(T entity, CancellationToken cancellationToken = default) { assignId(entity); Items.Add(entity); return Task.FromResult(entity); }
        public Task AddRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default) { foreach (var e in entities) { assignId(e); Items.Add(e); } return Task.CompletedTask; }
        public void Update(T entity) { }
        public void UpdateRange(IEnumerable<T> entities) { }
        public void Delete(T entity) => Items.Remove(entity);
        public void DeleteRange(IEnumerable<T> entities) { foreach (var e in entities.ToList()) Items.Remove(e); }
    }
}
