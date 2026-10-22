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
    private sealed class Db(DbContextOptions<ApplicationDbContext> options) : ApplicationDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder b)
        {
            Type[] types = [typeof(UserAccount), typeof(Story), typeof(StoryVersion), typeof(StoryGenerationJob),
                typeof(MediaContext), typeof(ModerationItem), typeof(ContentCategory)];
            foreach (var type in types)
                foreach (var p in type.GetProperties().Where(p => typeof(BaseEntity).IsAssignableFrom(p.PropertyType) ||
                    p.PropertyType.IsGenericType && p.PropertyType.GetGenericArguments().Any(t => typeof(BaseEntity).IsAssignableFrom(t))))
                    b.Entity(type).Ignore(p.Name);
            foreach (var type in b.Model.GetEntityTypes().Select(x => x.ClrType).ToArray())
                if (!types.Contains(type)) b.Ignore(type);
            foreach (var type in types) b.Entity(type).HasKey("Id");
            b.Entity<StoryGenerationJob>().HasIndex(x => x.OperationKey).IsUnique();
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
        db.Stories.Add(new() { Id = 1, AuthorUserId = 1, Scope = StoryScope.Creator, Source = StorySource.Manual,
            Status = StoryStatus.Draft, OutputMode = StoryOutputMode.ReadingMediaOnly, AuthoringRevisionToken = "rev1" });
        db.StoryVersions.Add(new() { Id = 10, StoryId = 1, IsCurrent = true, Title = "Na giúp bạn", Content = "Na cùng bạn sửa đồ chơi.",
            SourceDeclaration = "Tự viết", RightsDeclaration = "Tôi có quyền" });
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
}
