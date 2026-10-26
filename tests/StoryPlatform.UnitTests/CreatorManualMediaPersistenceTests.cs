using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.Supervision.Interfaces;
using StoryPlatform.Application.Features.CreatorAuthoring.Services;
using StoryPlatform.Application.Features.MediaGeneration.Services;
using StoryPlatform.Application.Features.Moderation;
using StoryPlatform.Application.Features.Moderation.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.Infrastructure.Persistence;
using StoryPlatform.Infrastructure.Persistence.Repositories;
using Xunit;

namespace StoryPlatform.UnitTests;

/// <summary>SQLite persistence/rollback with real EF repositories. Advisory locks and providers are test doubles.</summary>
public sealed class CreatorManualMediaPersistenceTests
{
    internal sealed class Db(DbContextOptions<ApplicationDbContext> options) : ApplicationDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder b)
        {
            Type[] types = [typeof(UserAccount), typeof(Story), typeof(StoryVersion), typeof(StoryGenerationJob),
                typeof(MediaContext), typeof(ModerationItem), typeof(ContentCategory), typeof(StoryScene),
                typeof(StorySegment), typeof(IllustrationBeat), typeof(MediaAsset), typeof(StoryAuthoringOperation), typeof(MediaAssetGenerationTask),
                typeof(LearningArtifactGenerationBatch)];
            foreach (var type in types)
                foreach (var p in type.GetProperties().Where(p => typeof(BaseEntity).IsAssignableFrom(p.PropertyType) ||
                    p.PropertyType.IsGenericType && p.PropertyType.GetGenericArguments().Any(t => typeof(BaseEntity).IsAssignableFrom(t))))
                    b.Entity(type).Ignore(p.Name);
            foreach (var type in b.Model.GetEntityTypes().Select(x => x.ClrType).ToArray())
                if (!types.Contains(type)) b.Ignore(type);
            foreach (var type in types) b.Entity(type).HasKey("Id");
            b.Entity<StoryGenerationJob>().HasIndex(x => x.OperationKey).IsUnique();
            b.Entity<StoryGenerationJob>().Property(x => x.ConcurrencyToken).IsConcurrencyToken();
            b.Entity<Story>().Property(x => x.AuthoringRevisionToken).IsConcurrencyToken();
            b.Entity<ModerationItem>().Property(x => x.RowVersion).IsConcurrencyToken();
            new StoryPlatform.Infrastructure.Persistence.Configurations.StoryVersionConfiguration().Configure(b.Entity<StoryVersion>());
            new StoryPlatform.Infrastructure.Persistence.Configurations.StoryAuthoringOperationConfiguration().Configure(b.Entity<StoryAuthoringOperation>());
            new StoryPlatform.Infrastructure.Persistence.Configurations.MediaAssetConfiguration().Configure(b.Entity<MediaAsset>());
            new StoryPlatform.Infrastructure.Persistence.Configurations.MediaAssetGenerationTaskConfiguration().Configure(b.Entity<MediaAssetGenerationTask>());
            new StoryPlatform.Infrastructure.Persistence.Configurations.LearningArtifactGenerationBatchConfiguration().Configure(b.Entity<LearningArtifactGenerationBatch>());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConfirmationPersistsOnePinnedJobOrRollsBackJobContextAndStatusIfAuditFails(bool failAudit)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new Db(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        db.Users.Add(new() { Id = 1, Role = UserRole.ContentCreator, Status = AccountStatus.Active });
        db.Stories.Add(new()
        {
            Id = 1,
            AuthorUserId = 1,
            Scope = StoryScope.Creator,
            Source = StorySource.Manual,
            Status = StoryStatus.Draft,
            OutputMode = StoryOutputMode.ReadingMediaOnly,
            AuthoringRevisionToken = "rev1"
        });
        db.StoryVersions.Add(new()
        {
            Id = 10,
            StoryId = 1,
            IsCurrent = true,
            Title = "Na giúp bạn",
            Content = "Na cùng bạn sửa đồ chơi.",
            AuthoringMetadata = new StoryVersionAuthoringMetadata
            {
                SourceDeclaration = "Tự viết",
                RightsDeclaration = "Tôi có quyền"
            }
        });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        await using var real = new UnitOfWork(db);
        var uow = new Mock<IUnitOfWork>();
        void Register<T>() where T : class => uow.Setup(x => x.Repository<T>()).Returns(real.Repository<T>());
        Register<UserAccount>(); Register<Story>(); Register<StoryVersion>(); Register<StoryGenerationJob>();
        Register<MediaContext>(); Register<ModerationItem>(); Register<ContentCategory>();
        uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns((CancellationToken ct) => real.SaveChangesAsync(ct));
        uow.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns((CancellationToken ct) => real.BeginTransactionAsync(ct));
        uow.Setup(x => x.CommitTransactionAsync(It.IsAny<CancellationToken>())).Returns((CancellationToken ct) => real.CommitTransactionAsync(ct));
        uow.Setup(x => x.RollbackTransactionAsync(It.IsAny<CancellationToken>())).Returns((CancellationToken ct) => real.RollbackTransactionAsync(ct));
        uow.Setup(x => x.AcquireTransactionLockAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var evaluator = new Mock<IGrayZoneEvaluator>();
        evaluator.SetupGet(x => x.IsConfigured).Returns(true);
        evaluator.Setup(x => x.CanReuseAsync(It.IsAny<GrayZoneAssessment>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        evaluator.Setup(x => x.EvaluateAsync(It.IsAny<GrayZoneScan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GrayZoneScan scan, CancellationToken _) => new GrayZoneAssessment(ModerationZone.Safe,
                GrayZoneEvaluator.Fingerprint(scan), scan.Phase, "test-policy", "test", "test", 0, []));
        var audit = new Mock<IAuditLogWriter>();
        if (failAudit) audit.Setup(x => x.LogAsync(1, "CreatorManualContentConfirmed", nameof(Story), 1,
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("Audit unavailable"));
        var checks = new GrayZoneContentCheckService(uow.Object, evaluator.Object,
            new GrayZoneWorkflowService(uow.Object, audit.Object), new Mock<ISupervisionAccessGuard>().Object);
        var service = new CreatorManualMediaService(uow.Object, checks, evaluator.Object, new MediaContextBuilder(), audit.Object, new());
        if (failAudit)
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ConfirmAsync(1, 1, new(10, "rev1")));
        else
        {
            var result = await service.ConfirmAsync(1, 1, new(10, "rev1"));
            db.ChangeTracker.Clear();
            Assert.Equal(result.JobId, (await service.ConfirmAsync(1, 1, new(10, "rev1"))).JobId);
        }
        db.ChangeTracker.Clear();
        Assert.Equal(failAudit ? 0 : 1, await db.StoryGenerationJobs.CountAsync());
        Assert.Equal(failAudit ? 0 : 1, await db.Set<MediaContext>().CountAsync());
        Assert.Equal(failAudit ? StoryStatus.Draft : StoryStatus.MediaProcessing, (await db.Stories.SingleAsync()).Status);
        Assert.Equal("Na cùng bạn sửa đồ chơi.", (await db.StoryVersions.SingleAsync()).Content);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MediaApprovalPersistsAtomicallyOrRollsBackOnCommitFailure(bool failCommit)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new Db(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        db.Users.Add(new() { Id = 1, Role = UserRole.ContentCreator, Status = AccountStatus.Active });
        db.Stories.Add(new()
        {
            Id = 1,
            AuthorUserId = 1,
            Scope = StoryScope.Creator,
            Source = StorySource.Manual,
            Status = StoryStatus.MediaReview
        });
        db.StoryVersions.Add(new()
        {
            Id = 10,
            StoryId = 1,
            IsCurrent = true,
            Title = "Story",
            Content = "Content",
            MediaState = new StoryVersionMediaState
            {
                MediaPackageRevision = 1
            }
        });
        db.StoryGenerationJobs.Add(new()
        {
            Id = 20,
            StoryId = 1,
            StoryVersionId = 10,
            Operation = GenerationJobOperation.GenerateMediaPackage,
            Status = GenerationJobStatus.Completed
        });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        await using var real = new UnitOfWork(db);
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.Repository<UserAccount>()).Returns(real.Repository<UserAccount>());
        uow.Setup(x => x.Repository<Story>()).Returns(real.Repository<Story>());
        uow.Setup(x => x.Repository<StoryVersion>()).Returns(real.Repository<StoryVersion>());
        uow.Setup(x => x.Repository<StoryGenerationJob>()).Returns(real.Repository<StoryGenerationJob>());
        uow.Setup(x => x.Repository<ModerationItem>()).Returns(real.Repository<ModerationItem>());
        uow.Setup(x => x.Repository<MediaAsset>()).Returns(real.Repository<MediaAsset>());
        uow.Setup(x => x.Repository<MediaAssetGenerationTask>()).Returns(real.Repository<MediaAssetGenerationTask>());
        uow.Setup(x => x.DiscardTrackedChanges()).Callback(real.DiscardTrackedChanges);
        uow.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns((CancellationToken ct) => real.BeginTransactionAsync(ct));
        uow.Setup(x => x.RollbackTransactionAsync(It.IsAny<CancellationToken>())).Returns((CancellationToken ct) => real.RollbackTransactionAsync(ct));
        uow.Setup(x => x.CommitTransactionAsync(It.IsAny<CancellationToken>())).Returns(async (CancellationToken ct) =>
        {
            await real.SaveChangesAsync(ct);
            if (failCommit) throw new InvalidOperationException("Commit failed");
            await real.CommitTransactionAsync(ct);
        });
        uow.Setup(x => x.AcquireTransactionLockAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var readiness = new Mock<StoryPlatform.Application.Features.MediaGeneration.Interfaces.IMediaReadinessService>();
        readiness.Setup(x => x.CheckAsync(10, null, It.IsAny<CancellationToken>())).ReturnsAsync(
            new StoryPlatform.Application.Features.MediaGeneration.Models.MediaReadinessResult(true, 1, 1, 1, 1, [], [], [], []));
        var service = MediaGenerationServiceTests.Create(uow.Object, readiness: readiness.Object);
        if (failCommit) await Assert.ThrowsAsync<InvalidOperationException>(() => service.ConfirmAsync(1, 1, new(10, 1)));
        else Assert.True((await service.ConfirmAsync(1, 1, new(10, 1))).IsReady);
        db.ChangeTracker.Clear();
        var saved = await db.StoryVersions.SingleAsync();
        Assert.Equal(failCommit ? null : 1, saved.MediaState.ConfirmedMediaPackageRevision);
        Assert.Equal(failCommit ? null : 1, saved.MediaState.MediaConfirmedByUserId);
        Assert.Equal(failCommit, saved.MediaState.MediaConfirmedAt is null);
        Assert.Equal(failCommit ? StoryStatus.MediaReview : StoryStatus.Ready, (await db.Stories.SingleAsync()).Status);
    }

    [Fact]
    public async Task SplitVersionRoundTripPreservesOwnedStateWhenUpdatingAnAlreadyTrackedVersion()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new Db(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        db.Users.AddRange(new UserAccount { Id = 1 }, new UserAccount { Id = 2 });
        db.Stories.Add(new() { Id = 1, AuthorUserId = 1, Scope = StoryScope.Creator });
        var added = db.StoryVersions.Add(new() { Id = 10, StoryId = 1, IsCurrent = true, Title = "Original", Content = "Content" }).Entity;
        var repository = new GenericRepository<StoryVersion>(db);
        added.AuthoringMetadata.SourceDeclaration = "Draft";
        repository.Update(added);
        Assert.Equal(EntityState.Added, db.Entry(added).State);
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var tracked = await repository.GetByIdAsync(10);
        Assert.NotNull(tracked);
        var detached = await repository.FirstOrDefaultAsync(x => x.Id == 10);
        Assert.NotNull(detached);
        Assert.NotSame(tracked, detached);
        detached.AuthoringMetadata.SourceDeclaration = "Author";
        detached.AuthoringMetadata.RightsDeclaration = "Own work";
        detached.AuthoringMetadata.StoryId = 999;
        detached.Moderation.ReviewStatus = VersionReviewStatus.Pending;
        detached.Moderation.ReviewedByUserId = 2;
        detached.Moderation.GrayZoneClearedByUserId = 2;
        detached.MediaState.MediaPackageRevision = 1;
        detached.MediaState.ConfirmedMediaPackageRevision = 1;
        detached.MediaState.MediaConfirmedByUserId = 1;
        detached.MediaState.MediaConfirmedAt = DateTime.UtcNow;
        repository.Update(detached);
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var saved = await repository.FirstOrDefaultAsync(x => x.Id == 10);
        Assert.Equal("Author", saved!.AuthoringMetadata.SourceDeclaration);
        Assert.Equal("Own work", saved.AuthoringMetadata.RightsDeclaration);
        Assert.Equal(1, saved.AuthoringMetadata.StoryId);
        Assert.Equal(10, saved.AuthoringMetadata.StoryVersionId);
        Assert.Equal(VersionReviewStatus.Pending, saved.Moderation.ReviewStatus);
        Assert.Equal(2, saved.Moderation.ReviewedByUserId);
        Assert.Equal(2, saved.Moderation.GrayZoneClearedByUserId);
        Assert.Equal(1, saved.MediaState.MediaConfirmedByUserId);
        Assert.Equal(1, saved.MediaState.ConfirmedMediaPackageRevision);
        Assert.NotNull(saved.MediaState.MediaConfirmedAt);
        Assert.Equal("Original", saved.Title);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AuthoringReceiptsAllowMultipleCommandsButRejectDuplicateKeysAndMismatchedStories(bool wrongStory)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new Db(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        db.Users.Add(new() { Id = 1 });
        db.Stories.Add(new() { Id = 1, AuthorUserId = 1, Scope = StoryScope.Creator });
        db.StoryVersions.Add(new() { Id = 10, StoryId = 1, IsCurrent = true, Title = "Title" });
        await db.SaveChangesAsync();
        db.StoryAuthoringOperations.AddRange(new StoryAuthoringOperation { StoryId = 1, StoryVersionId = 10, ActorUserId = 1,
                Kind = "save-manual", OperationId = "save-1", RequestHash = "hash1", OperationResponseJson = "{}" },
            new StoryAuthoringOperation { StoryId = 1, StoryVersionId = 10, ActorUserId = 1,
                Kind = "confirm-import", OperationId = "save-2", RequestHash = "hash2", OperationResponseJson = "{}" });
        await db.SaveChangesAsync();
        Assert.Equal(2, await db.StoryAuthoringOperations.CountAsync());
        db.StoryAuthoringOperations.Add(new() { StoryId = wrongStory ? 2 : 1, StoryVersionId = 10, ActorUserId = 1,
            Kind = "save-manual", OperationId = wrongStory ? "save-3" : "save-1", RequestHash = "other", OperationResponseJson = "{}" });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task RevisionAfterApprovedRemovalPersistsWithStoryRevisionToken()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new Db(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        db.Users.Add(new() { Id = 1, Role = UserRole.ContentCreator, Status = AccountStatus.Active });
        db.Stories.Add(new()
        {
            Id = 1, AuthorUserId = 1, Scope = StoryScope.Creator, Source = StorySource.Manual, Status = StoryStatus.Ready,
            OutputMode = StoryOutputMode.ReadingMediaOnly, AuthoringRevisionToken = "rev1"
        });
        db.StoryVersions.Add(new()
        {
            Id = 10, StoryId = 1, VersionNo = 1, IsCurrent = true, Title = "Rùa đúng hẹn", Content = "Rùa giữ lời hứa.",
            AuthoringMetadata = new StoryVersionAuthoringMetadata { SourceDeclaration = "Tự viết", RightsDeclaration = "Tôi có quyền" },
            Moderation = new() { ReviewStatus = VersionReviewStatus.Approved }
        });
        db.Set<ModerationItem>().Add(new()
        {
            Id = 1, ItemType = ModerationItemType.PublicStoryRemoval, Status = ModerationItemStatus.Approved,
            StoryId = 1, StoryVersionId = 10, CreatedByUserId = 1, RemovedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        await using var real = new UnitOfWork(db);
        var uow = new Mock<IUnitOfWork>();
        void Register<T>() where T : class => uow.Setup(x => x.Repository<T>()).Returns(real.Repository<T>());
        void Empty<T>() where T : class
        {
            var repository = new Mock<IGenericRepository<T>>();
            repository.Setup(x => x.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<T, bool>>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<T>());
            uow.Setup(x => x.Repository<T>()).Returns(repository.Object);
        }
        Register<UserAccount>(); Register<Story>(); Register<StoryVersion>(); Register<ModerationItem>();
        Empty<StoryVocabulary>(); Empty<QuizItem>(); Empty<DiscussionQuestion>();
        uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns((CancellationToken ct) => real.SaveChangesAsync(ct));
        uow.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns((CancellationToken ct) => real.BeginTransactionAsync(ct));
        uow.Setup(x => x.CommitTransactionAsync(It.IsAny<CancellationToken>())).Returns((CancellationToken ct) => real.CommitTransactionAsync(ct));
        uow.Setup(x => x.RollbackTransactionAsync(It.IsAny<CancellationToken>())).Returns((CancellationToken ct) => real.RollbackTransactionAsync(ct));
        uow.Setup(x => x.AcquireTransactionLockAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var service = new CreatorPublicationService(uow.Object,
            new Mock<StoryPlatform.Application.Features.Moderation.Interfaces.IModerationQueueService>().Object, new Mock<IAuditLogWriter>().Object);

        var result = await service.CreateRevisionAsync(1, 1, new() { SourceVersionId = 10 });

        db.ChangeTracker.Clear();
        var story = await db.Stories.SingleAsync();
        Assert.Equal(StoryStatus.Draft, story.Status);
        Assert.Equal(result.RevisionToken, story.AuthoringRevisionToken);
        Assert.NotEqual("rev1", story.AuthoringRevisionToken);
        Assert.Equal(result.VersionId, (await db.StoryVersions.SingleAsync(x => x.IsCurrent)).Id);
    }

    [Fact]
    public async Task PublicStoryWithdrawPersistsWithModerationRowVersion()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new Db(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        db.Users.Add(new() { Id = 1, Role = UserRole.ContentCreator, Status = AccountStatus.Active });
        db.Stories.Add(new()
        {
            Id = 1, AuthorUserId = 1, Scope = StoryScope.Creator, Source = StorySource.Manual, Status = StoryStatus.Ready,
            OutputMode = StoryOutputMode.ReadingMediaOnly, AuthoringRevisionToken = "rev1"
        });
        db.StoryVersions.Add(new()
        {
            Id = 10, StoryId = 1, VersionNo = 1, IsCurrent = true, Title = "Hạt giống", Content = "Lan gieo hạt.",
            AuthoringMetadata = new StoryVersionAuthoringMetadata { SourceDeclaration = "Tự viết", RightsDeclaration = "Tôi có quyền" },
            Moderation = new() { ReviewStatus = VersionReviewStatus.Pending, ReviewSubmittedAt = DateTime.UtcNow }
        });
        db.Set<ModerationItem>().Add(new()
        {
            Id = 1, ItemType = ModerationItemType.PublicStoryReview, Status = ModerationItemStatus.Pending,
            StoryId = 1, StoryVersionId = 10, CreatedByUserId = 1, RowVersion = 0
        });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        await using var real = new UnitOfWork(db);
        var uow = new Mock<IUnitOfWork>();
        void Register<T>() where T : class => uow.Setup(x => x.Repository<T>()).Returns(real.Repository<T>());
        Register<UserAccount>(); Register<Story>(); Register<StoryVersion>(); Register<ModerationItem>();
        uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns((CancellationToken ct) => real.SaveChangesAsync(ct));
        uow.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns((CancellationToken ct) => real.BeginTransactionAsync(ct));
        uow.Setup(x => x.CommitTransactionAsync(It.IsAny<CancellationToken>())).Returns((CancellationToken ct) => real.CommitTransactionAsync(ct));
        uow.Setup(x => x.RollbackTransactionAsync(It.IsAny<CancellationToken>())).Returns((CancellationToken ct) => real.RollbackTransactionAsync(ct));
        uow.Setup(x => x.AcquireTransactionLockAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var service = new PublicStorySubmissionService(uow.Object,
            new Mock<StoryPlatform.Application.Features.Moderation.Interfaces.IModerationQueueService>().Object,
            new Mock<StoryPlatform.Application.Features.MediaGeneration.Interfaces.IMediaReadinessService>().Object,
            new Mock<IAuditLogWriter>().Object);

        await service.WithdrawAsync(1, 1);

        db.ChangeTracker.Clear();
        var item = await db.Set<ModerationItem>().SingleAsync();
        Assert.Equal(ModerationItemStatus.Withdrawn, item.Status);
        Assert.Equal(1, item.RowVersion);
        Assert.NotNull(item.WithdrawnAt);
        Assert.Equal(VersionReviewStatus.None, (await db.StoryVersions.SingleAsync()).Moderation.ReviewStatus);
    }

}
