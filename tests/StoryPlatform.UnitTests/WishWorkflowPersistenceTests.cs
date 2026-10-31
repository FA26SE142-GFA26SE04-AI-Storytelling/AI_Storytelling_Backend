using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.AuditLogs.Services;
using StoryPlatform.Application.Features.Notifications.DTOs;
using StoryPlatform.Application.Features.Notifications.Interfaces;
using StoryPlatform.Application.Features.Notifications.Services;
using StoryPlatform.Application.Features.StoryWishes.Services;
using StoryPlatform.Application.Features.WishCards;
using StoryPlatform.Application.Features.WishCards.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.Infrastructure.Persistence;
using StoryPlatform.Infrastructure.Persistence.Configurations;
using StoryPlatform.Infrastructure.Persistence.Repositories;
using Xunit;

namespace StoryPlatform.UnitTests;

// Real relational storage/rollback; PostgreSQL advisory locks are replaced only in this SQLite fixture.
public sealed class WishWorkflowPersistenceTests
{
    [Theory]
    [InlineData("none")]
    [InlineData("store")]
    [InlineData("commit")]
    [InlineData("publish")]
    [InlineData("cancelBeforeCommit")]
    [InlineData("cancelAfterCommit")]
    public async Task Reminder_is_atomic_and_realtime_never_precedes_commit(string failure)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new WishDb(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await SeedAsync(db);
        using var cancellation = new CancellationTokenSource();
        await using var uow = new TestUnitOfWork(db, failure, cancellation);
        var publishedAfterCommit = false;
        var publisher = new Mock<INotificationRealtimePublisher>();
        publisher.Setup(x => x.PublishAsync(7, It.IsAny<NotificationDto>(), It.IsAny<CancellationToken>()))
            .Returns(async (int _, NotificationDto dto, CancellationToken _) =>
            {
                publishedAfterCommit = db.Database.CurrentTransaction is null &&
                    await db.Set<Notification>().AsNoTracking().AnyAsync(x => x.Id == dto.Id) &&
                    await db.Set<StoryWish>().AsNoTracking().AnyAsync(x => x.Id == 1 && x.ExpiryWarnedAt != null);
                if (failure == "publish") throw new InvalidOperationException("simulated SignalR failure");
            });
        var notifier = new StoryWishNotifier(uow, new NotificationService(uow, publisher.Object), publisher.Object,
            NullLogger<StoryWishNotifier>.Instance);
        var sweep = new StoryWishSweepService(uow, notifier, TimeProvider.System);
        if (failure is "store" or "commit" or "cancelBeforeCommit")
        {
            if (failure == "cancelBeforeCommit")
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sweep.SweepAsync(cancellation.Token));
            else
                await Assert.ThrowsAsync<InvalidOperationException>(() => sweep.SweepAsync(cancellation.Token));
            db.ChangeTracker.Clear();
            Assert.Empty(await db.Set<Notification>().ToListAsync());
            Assert.Null((await db.Set<StoryWish>().SingleAsync()).ExpiryWarnedAt);
            publisher.VerifyNoOtherCalls();
        }
        else
        {
            Assert.Equal(1, (await sweep.SweepAsync(cancellation.Token)).Warned);
            Assert.Equal(failure != "cancelAfterCommit", publishedAfterCommit);
            db.ChangeTracker.Clear();
            Assert.Single(await db.Set<Notification>().ToListAsync());
            Assert.NotNull((await db.Set<StoryWish>().SingleAsync()).ExpiryWarnedAt);
            Assert.Equal(0, (await sweep.SweepAsync()).Warned);
            publisher.Verify(x => x.PublishAsync(7, It.IsAny<NotificationDto>(), It.IsAny<CancellationToken>()),
                failure == "cancelAfterCommit" ? Times.Never() : Times.Once());
            Assert.Single(await new NotificationService(uow, publisher.Object).ListAsync(7));
        }
    }

    [Fact]
    public async Task Audit_reader_detects_add_remove_and_filters_other_stories_without_parsing_payloads()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new WishDb(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await SeedAsync(db);
        await using var uow = new TestUnitOfWork(db);
        var reader = new WishCardMappingChangeReader(db);
        var watermark = await reader.ReadWatermarkAsync(default);
        Assert.Equal(0, watermark);
        var empty = WishCardMappingAudit.Fingerprint(Array.Empty<StoryWishCardMatch>());
        var manual = new WishCardMatchService(uow, new AuditLogWriter(uow));
        await manual.AddAsync(7, 20, 1);
        var afterAdd = await reader.ReadWatermarkAsync(default);
        await manual.AddAsync(7, 20, 1); // no-op must not advance the watermark
        Assert.Equal(afterAdd, await reader.ReadWatermarkAsync(default));
        await manual.RemoveAsync(7, 20, 1);
        Assert.Empty(await db.Set<StoryWishCardMatch>().ToListAsync());
        Assert.Equal(empty, WishCardMappingAudit.Fingerprint(await db.Set<StoryWishCardMatch>().ToListAsync()));
        Assert.True(await reader.HasChangesAfterAsync(20, watermark, default));
        Assert.False(await reader.HasChangesAfterAsync(21, watermark, default));
        Assert.Equal(2, await db.Set<AuditLog>().CountAsync(x => x.Action == WishCardMappingAudit.Action));
    }

    [Fact]
    public async Task Marker_failure_rolls_back_mapping_and_its_business_audit()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new WishDb(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await SeedAsync(db);
        await using var uow = new TestUnitOfWork(db);
        var writer = new AuditLogWriter(uow);
        var audit = new Mock<IAuditLogWriter>();
        audit.Setup(x => x.LogAsync(It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .Returns((int? actor, string action, string entity, int id, object? before, object? after, CancellationToken ct) =>
                action == WishCardMappingAudit.Action ? Task.FromException(new InvalidOperationException("simulated marker failure")) :
                writer.LogAsync(actor, action, entity, id, before, after, ct));
        var manual = new WishCardMatchService(uow, audit.Object);
        await Assert.ThrowsAsync<InvalidOperationException>(() => manual.AddAsync(7, 20, 1));
        db.ChangeTracker.Clear();
        Assert.Empty(await db.Set<StoryWishCardMatch>().ToListAsync());
        Assert.Empty(await db.Set<AuditLog>().ToListAsync());
    }

    private static async Task SeedAsync(WishDb db)
    {
        await db.Database.EnsureCreatedAsync();
        db.Set<UserAccount>().Add(new() { Id = 7, Role = UserRole.User, Status = AccountStatus.Active });
        db.Set<ChildProfile>().Add(new() { Id = 1, OwnerUserId = 7, Status = ChildProfileStatus.Active });
        db.Set<StoryWish>().Add(new() { Id = 1, ChildProfileId = 1, Status = StoryWishStatus.Pending,
            PendingSince = DateTime.UtcNow.AddDays(-6.2), SubmittedAt = DateTime.UtcNow.AddDays(-6.2) });
        db.Set<Story>().Add(new() { Id = 20, AuthorUserId = 7, Scope = StoryScope.Creator, Status = StoryStatus.Ready });
        db.Set<WishCard>().Add(new() { Id = 1, Group = WishCardGroup.Character, Label = "Mèo", ImageUrl = "https://test.invalid/cat.png" });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    private sealed class TestUnitOfWork(ApplicationDbContext db, string failure = "none", CancellationTokenSource? cancellation = null) : IUnitOfWork
    {
        private readonly UnitOfWork _inner = new(db);
        public IGenericRepository<T> Repository<T>() where T : class => _inner.Repository<T>();
        public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            var count = await _inner.SaveChangesAsync(cancellationToken);
            if (failure == "store") throw new InvalidOperationException("simulated store failure");
            return count;
        }
        public Task BeginTransactionAsync(CancellationToken cancellationToken = default) => _inner.BeginTransactionAsync(cancellationToken);
        public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
        {
            if (failure == "commit") throw new InvalidOperationException("simulated commit failure");
            if (failure == "cancelBeforeCommit") cancellation!.Cancel();
            await _inner.CommitTransactionAsync(cancellationToken);
            if (failure == "cancelAfterCommit") cancellation!.Cancel();
        }
        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => _inner.RollbackTransactionAsync(cancellationToken);
        public Task AcquireTransactionLockAsync(int resourceId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void DiscardTrackedChanges() => _inner.DiscardTrackedChanges();
        public void Dispose() => _inner.Dispose();
        public ValueTask DisposeAsync() => _inner.DisposeAsync();
    }

    private sealed class WishDb(DbContextOptions<ApplicationDbContext> options) : ApplicationDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder b)
        {
            Type[] types = [typeof(UserAccount), typeof(ChildProfile), typeof(StoryWish), typeof(Story), typeof(WishCard),
                typeof(StoryWishCardMatch), typeof(AuditLog), typeof(Notification),
                typeof(InterventionCase)];
            foreach (var type in types)
                foreach (var p in type.GetProperties().Where(p => typeof(BaseEntity).IsAssignableFrom(p.PropertyType) ||
                    p.PropertyType.IsGenericType && p.PropertyType.GetGenericArguments().Any(t => typeof(BaseEntity).IsAssignableFrom(t))))
                    b.Entity(type).Ignore(p.Name);
            foreach (var type in b.Model.GetEntityTypes().Select(x => x.ClrType).ToArray())
                if (!types.Contains(type)) b.Ignore(type);
            foreach (var type in types) b.Entity(type).HasKey("Id");
            new AuditLogConfiguration().Configure(b.Entity<AuditLog>());
            new NotificationConfiguration().Configure(b.Entity<Notification>());
            new StoryWishCardMatchConfiguration().Configure(b.Entity<StoryWishCardMatch>());
        }
    }
}
