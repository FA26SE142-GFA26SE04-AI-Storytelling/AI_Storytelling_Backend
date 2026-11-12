using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.Moderation.Services;
using StoryPlatform.Application.Features.Notifications.DTOs;
using StoryPlatform.Application.Features.Notifications.Interfaces;
using StoryPlatform.Application.Features.Notifications.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.Infrastructure.Persistence;
using StoryPlatform.Infrastructure.Persistence.Repositories;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed class CreatorRemovalPersistenceTests
{
    private sealed class Db(DbContextOptions<ApplicationDbContext> options) : ApplicationDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder b)
        {
            Type[] types = [typeof(UserAccount), typeof(Story), typeof(StoryVersion), typeof(ModerationItem), typeof(Notification)];
            foreach (var type in types)
                foreach (var p in type.GetProperties().Where(p => typeof(BaseEntity).IsAssignableFrom(p.PropertyType) ||
                    p.PropertyType.IsGenericType && p.PropertyType.GetGenericArguments().Any(t => typeof(BaseEntity).IsAssignableFrom(t))))
                    b.Entity(type).Ignore(p.Name);
            foreach (var type in b.Model.GetEntityTypes().Select(x => x.ClrType).ToArray())
                if (!types.Contains(type)) b.Ignore(type);
            foreach (var type in types) b.Entity(type).HasKey("Id");
            b.Entity<Story>().Property(x => x.AuthoringRevisionToken).IsConcurrencyToken();
            b.Entity<ModerationItem>().Property(x => x.RowVersion).IsConcurrencyToken();
            b.Entity<ModerationItem>().HasOne(x => x.Story).WithMany().HasForeignKey(x => x.StoryId);
            b.Entity<ModerationItem>().HasOne(x => x.StoryVersion).WithMany().HasForeignKey(x => x.StoryVersionId);
            b.Entity<StoryVersion>().OwnsOne(x => x.AuthoringMetadata, owned => owned.ToTable("authoring_metadata"));
            b.Entity<StoryVersion>().OwnsOne(x => x.MediaState, owned => owned.ToTable("media_states"));
            b.Entity<StoryVersion>().OwnsOne(x => x.Moderation, owned =>
            {
                owned.ToTable("moderation_states");
                owned.Ignore(x => x.GrayZoneClearedByUser);
                owned.Ignore(x => x.ReviewedByUser);
            });
        }
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task Decision_after_opening_claim_persists_with_real_tracking(bool approve, bool failNotification)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new Db(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        db.Users.Add(new() { Id = 2, Role = UserRole.Moderator, Status = AccountStatus.Active });
        db.Users.Add(new() { Id = 1, Role = UserRole.ContentCreator, Status = AccountStatus.Active });
        db.Stories.Add(new() { Id = 44, Scope = StoryScope.Creator, AuthorUserId = 1,
            IsPublished = true, AuthoringRevisionToken = "original", ChildVisibleAt = DateTime.UtcNow });
        db.StoryVersions.Add(new() { Id = 90, StoryId = 44, IsCurrent = true, Title = "Story", Content = "Content" });
        db.Set<ModerationItem>().Add(new() { Id = 23, StoryId = 44, StoryVersionId = 90, CreatedByUserId = 1,
            ItemType = ModerationItemType.PublicStoryRemoval, Status = ModerationItemStatus.Claimed,
            ClaimedByUserId = 2, ClaimedAt = DateTime.UtcNow, RowVersion = 1 });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        await using var real = new UnitOfWork(db);
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.Repository<UserAccount>()).Returns(real.Repository<UserAccount>());
        uow.Setup(x => x.Repository<Story>()).Returns(real.Repository<Story>());
        uow.Setup(x => x.Repository<StoryVersion>()).Returns(real.Repository<StoryVersion>());
        uow.Setup(x => x.Repository<ModerationItem>()).Returns(real.Repository<ModerationItem>());
        uow.Setup(x => x.Repository<Notification>()).Returns(real.Repository<Notification>());
        uow.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns((CancellationToken ct) => real.BeginTransactionAsync(ct));
        // PostgreSQL advisory lock alone is replaced; queries, tracking and transactions are real.
        uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns((CancellationToken ct) => real.SaveChangesAsync(ct));
        uow.Setup(x => x.CommitTransactionAsync(It.IsAny<CancellationToken>())).Returns((CancellationToken ct) => real.CommitTransactionAsync(ct));
        uow.Setup(x => x.RollbackTransactionAsync(It.IsAny<CancellationToken>())).Returns((CancellationToken ct) => real.RollbackTransactionAsync(ct));
        var audit = new Mock<IAuditLogWriter>();
        var queue = new ModerationQueueService(uow.Object, audit.Object);
        var publisher = new Mock<INotificationRealtimePublisher>();
        var storage = new NotificationService(uow.Object, publisher.Object);
        var notifications = new Mock<INotificationService>();
        notifications.Setup(x => x.CreateStoredAsync(It.IsAny<int>(), It.IsAny<NotificationType>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(async (int recipient, NotificationType type, string? payload, CancellationToken ct) =>
            {
                var dto = await storage.CreateStoredAsync(recipient, type, payload, ct);
                if (failNotification) throw new InvalidOperationException("Notification storage failed after save");
                return dto;
            });
        var service = new CreatorPublicationService(uow.Object, queue, audit.Object, notifications: notifications.Object, publisher: publisher.Object);

        if (failNotification)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.DecideRemovalAsync(2, 23,
                new() { Approve = approve, Reason = "Decision", RowVersion = 1 }));
            db.ChangeTracker.Clear();
            Assert.True((await db.Stories.SingleAsync()).IsPublished);
            Assert.Equal(ModerationItemStatus.Claimed, (await db.Set<ModerationItem>().SingleAsync()).Status);
            Assert.Empty(await db.Set<Notification>().ToListAsync());
            publisher.Verify(x => x.PublishAsync(It.IsAny<int>(), It.IsAny<NotificationDto>(), It.IsAny<CancellationToken>()), Times.Never);
            return;
        }

        var result = await service.DecideRemovalAsync(2, 23, new() { Approve = approve, Reason = "Decision", RowVersion = 1 });

        db.ChangeTracker.Clear();
        var item = await db.Set<ModerationItem>().SingleAsync();
        var story = await db.Stories.SingleAsync();
        Assert.Equal(approve ? "Approved" : "Rejected", result.Status);
        Assert.Equal(2, item.RowVersion);
        Assert.Equal(2, item.DecidedByUserId);
        Assert.NotNull(item.DecidedAt);
        Assert.Equal("Decision", item.DecisionReason);
        Assert.Equal(!approve, story.IsPublished);
        Assert.Equal(approve, item.RemovedAt.HasValue);
        if (!approve) Assert.Equal("original", story.AuthoringRevisionToken);
        if (approve) { Assert.Null(story.ChildVisibleAt); Assert.NotEqual("original", story.AuthoringRevisionToken); }
        Assert.Equal("Content", (await db.StoryVersions.SingleAsync()).Content);
        Assert.Equal(1, (await db.Set<Notification>().SingleAsync()).RecipientUserId);
        publisher.Verify(x => x.PublishAsync(1, It.IsAny<NotificationDto>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
