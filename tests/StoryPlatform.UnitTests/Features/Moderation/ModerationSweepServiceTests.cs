using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Moq;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Infrastructure.Persistence;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.Moderation.Services;
using StoryPlatform.Application.Features.Notifications.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.Moderation;

public class ModerationSweepServiceTests
{
    private readonly Mock<IGenericRepository<ModerationItem>> _itemRepoMock = new();
    private readonly Mock<IGenericRepository<UserAccount>> _userRepoMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly Mock<INotificationService> _notificationServiceMock = new();
    private readonly ModerationSweepService _sut;

    public ModerationSweepServiceTests()
    {
        _unitOfWorkMock.Setup(u => u.Repository<ModerationItem>()).Returns(_itemRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.Repository<UserAccount>()).Returns(_userRepoMock.Object);
        _sut = new ModerationSweepService(_unitOfWorkMock.Object, _notificationServiceMock.Object);
    }

    [Fact]
    public async Task ReleaseExpiredClaimsAsync_OnlyExpiredClaimsAreReleased()
    {
        var expired = ClaimedItem(1, 11);
        var recent = ClaimedItem(2, 11);
        recent.ClaimedAt = DateTime.UtcNow.AddMinutes(-10);
        var finished = ClaimedItem(3, 11);
        finished.Status = ModerationItemStatus.Approved;
        var noTimestamp = ClaimedItem(4, 11);
        noTimestamp.ClaimedAt = null;
        ReturnItems(expired, recent, finished, noTimestamp);

        Assert.Equal(1, await _sut.ReleaseExpiredClaimsAsync());
        Assert.Equal(ModerationItemStatus.Claimed, recent.Status);
        Assert.Equal(ModerationItemStatus.Approved, finished.Status);
        Assert.Equal(ModerationItemStatus.Claimed, noTimestamp.Status);
    }

    [Fact]
    public async Task ReleaseClaimsOfModeratorAsync_DoesNotReleaseAnotherModeratorsClaim()
    {
        var own = ClaimedItem(1, 11);
        var other = ClaimedItem(2, 12);
        ReturnItems(own, other);

        Assert.Equal(1, await _sut.ReleaseClaimsOfModeratorAsync(11));
        Assert.Equal(ModerationItemStatus.Claimed, other.Status);
    }

    [Fact]
    public async Task ReleaseExpiredClaimsAsync_ItemChangedSinceScan_SkipsIt()
    {
        ReturnItems(ClaimedItem(1, 11));
        var reclaimed = ClaimedItem(1, 12);
        reclaimed.RowVersion = 6;
        reclaimed.ClaimedAt = DateTime.UtcNow;
        _itemRepoMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(reclaimed);

        Assert.Equal(0, await _sut.ReleaseExpiredClaimsAsync());
        Assert.Equal(12, reclaimed.ClaimedByUserId);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReleaseExpiredClaimsAsync_PreservesOriginalEfConcurrencyToken()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=metadata_only;Username=unused;Password=unused").Options;
        await using var context = new ApplicationDbContext(options);
        var detached = ClaimedItem(1, 11);
        ReturnItems(detached);
        var tracked = ClaimedItem(1, 11);
        context.Attach(tracked);
        _itemRepoMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(tracked);

        Assert.Equal(1, await _sut.ReleaseExpiredClaimsAsync());

        context.ChangeTracker.DetectChanges();
        var token = context.Entry(tracked).Property(item => item.RowVersion);
        Assert.Equal(4, token.OriginalValue);
        Assert.Equal(5, token.CurrentValue);
        Assert.Equal(ModerationItemStatus.Claimed, detached.Status);
    }

    [Fact]
    public async Task WarnOverdueSlaAsync_RepeatedScan_NotifiesOnlyOnceAndOnlyActiveModerators()
    {
        var overdue = new ModerationItem
        {
            Id = 3, Status = ModerationItemStatus.Pending, ItemType = ModerationItemType.ContentReport,
            SlaDueAt = DateTime.UtcNow.AddHours(-1), RowVersion = 4
        };
        ReturnItems(overdue);
        var suspended = Moderator(12);
        suspended.Status = AccountStatus.Suspended;
        var parent = Moderator(13);
        parent.Role = UserRole.User;
        ReturnModerators(Moderator(11), suspended, parent);

        Assert.Equal(1, await _sut.WarnOverdueSlaAsync());
        Assert.Equal(0, await _sut.WarnOverdueSlaAsync());
        Assert.Equal(5, overdue.RowVersion);
        _notificationServiceMock.Verify(n => n.CreateAsync(
            11, NotificationType.ModerationSlaWarning, It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        _notificationServiceMock.Verify(n => n.CreateAsync(
            It.Is<int>(id => id != 11), It.IsAny<NotificationType>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWorkMock.Verify(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WarnOverdueSlaAsync_OnlyUnwarnedOpenOverdueItemsAreNotified()
    {
        var now = DateTime.UtcNow;
        var items = new[]
        {
            new ModerationItem { Id = 1, Status = ModerationItemStatus.Pending, SlaDueAt = now.AddHours(-1) },
            new ModerationItem { Id = 2, Status = ModerationItemStatus.Claimed, SlaDueAt = now.AddHours(-1) },
            new ModerationItem { Id = 3, Status = ModerationItemStatus.Approved, SlaDueAt = now.AddHours(-1) },
            new ModerationItem { Id = 4, Status = ModerationItemStatus.Pending, SlaDueAt = now.AddHours(1) },
            new ModerationItem { Id = 5, Status = ModerationItemStatus.Pending, SlaDueAt = null },
            new ModerationItem { Id = 6, Status = ModerationItemStatus.Pending, SlaDueAt = now.AddHours(-1), SlaWarnedAt = now }
        };
        ReturnItems(items);
        ReturnModerators(Moderator(11));

        Assert.Equal(2, await _sut.WarnOverdueSlaAsync());
        _notificationServiceMock.Verify(n => n.CreateAsync(
            11, NotificationType.ModerationSlaWarning, It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task WarnOverdueSlaAsync_NotificationFails_RollsBackWholeBatch()
    {
        ReturnItems(new ModerationItem
        {
            Id = 3, Status = ModerationItemStatus.Pending, SlaDueAt = DateTime.UtcNow.AddHours(-1)
        });
        ReturnModerators(Moderator(11), Moderator(12));
        _notificationServiceMock.Setup(n => n.CreateAsync(
                12, NotificationType.ModerationSlaWarning, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Notification failed"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.WarnOverdueSlaAsync());

        _unitOfWorkMock.Verify(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWorkMock.Verify(u => u.RollbackTransactionAsync(CancellationToken.None), Times.Once);
        _unitOfWorkMock.Verify(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WarnOverdueSlaAsync_ConcurrentWorkerWins_RollsBackWithoutNotifications()
    {
        ReturnItems(new ModerationItem
        {
            Id = 3, Status = ModerationItemStatus.Pending, SlaDueAt = DateTime.UtcNow.AddHours(-1)
        });
        ReturnModerators(Moderator(11));
        _unitOfWorkMock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConflictException("Conflict"));

        await Assert.ThrowsAsync<ConflictException>(() => _sut.WarnOverdueSlaAsync());

        _notificationServiceMock.Verify(n => n.CreateAsync(
            It.IsAny<int>(), It.IsAny<NotificationType>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWorkMock.Verify(u => u.RollbackTransactionAsync(CancellationToken.None), Times.Once);
    }

    private void ReturnItems(params ModerationItem[] items)
    {
        _itemRepoMock
            .Setup(r => r.FindAsync(It.IsAny<Expression<Func<ModerationItem, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<ModerationItem, bool>> predicate, string? _, CancellationToken _) =>
                items.Where(predicate.Compile()).ToList());
        foreach (var item in items)
        {
            _itemRepoMock.Setup(r => r.GetByIdAsync(item.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(item);
        }
    }

    private void ReturnModerators(params UserAccount[] moderators) =>
        _userRepoMock
            .Setup(r => r.FindAsync(It.IsAny<Expression<Func<UserAccount, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<UserAccount, bool>> predicate, string? _, CancellationToken _) =>
                moderators.Where(predicate.Compile()).ToList());

    private static ModerationItem ClaimedItem(int id, int claimedBy) => new()
    {
        Id = id, ItemType = ModerationItemType.GrayZoneStory, Status = ModerationItemStatus.Claimed,
        ClaimedByUserId = claimedBy, ClaimedAt = DateTime.UtcNow.AddHours(-3), RowVersion = 4
    };

    private static UserAccount Moderator(int id) => new()
    {
        Id = id, Role = UserRole.Moderator, Status = AccountStatus.Active
    };

    [Fact]
    public async Task ReleaseExpiredClaimsAsync_NothingExpired_ReturnsZeroAndDoesNotSave()
    {
        ReturnItems();

        var count = await _sut.ReleaseExpiredClaimsAsync();

        Assert.Equal(0, count);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReleaseExpiredClaimsAsync_ExpiredClaims_ReturnsItemsToPendingAndBumpsRowVersion()
    {
        var item = ClaimedItem(1, claimedBy: 11);
        ReturnItems(item);

        var count = await _sut.ReleaseExpiredClaimsAsync();

        Assert.Equal(1, count);
        Assert.Equal(ModerationItemStatus.Pending, item.Status);
        Assert.Null(item.ClaimedByUserId);
        Assert.Null(item.ClaimedAt);
        Assert.Equal(5, item.RowVersion);
        _itemRepoMock.Verify(r => r.Update(item), Times.Once);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReleaseClaimsOfModeratorAsync_ReleasesAllClaimsOfThatModerator()
    {
        var first = ClaimedItem(1, claimedBy: 11);
        var second = ClaimedItem(2, claimedBy: 11);
        ReturnItems(first, second);

        var count = await _sut.ReleaseClaimsOfModeratorAsync(11);

        Assert.Equal(2, count);
        Assert.All(new[] { first, second }, item =>
        {
            Assert.Equal(ModerationItemStatus.Pending, item.Status);
            Assert.Null(item.ClaimedByUserId);
        });
    }

    [Fact]
    public async Task WarnOverdueSlaAsync_NoOverdueItems_ReturnsZeroAndSendsNothing()
    {
        ReturnItems();
        ReturnModerators(Moderator(11));

        var count = await _sut.WarnOverdueSlaAsync();

        Assert.Equal(0, count);
        _notificationServiceMock.Verify(n => n.CreateAsync(
            It.IsAny<int>(), It.IsAny<NotificationType>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WarnOverdueSlaAsync_OverdueItem_MarksWarnedAndNotifiesEveryActiveModerator()
    {
        var overdue = new ModerationItem
        {
            Id = 3, ItemType = ModerationItemType.ContentReport, Status = ModerationItemStatus.Pending,
            SlaDueAt = DateTime.UtcNow.AddHours(-2), SlaWarnedAt = null
        };
        ReturnItems(overdue);
        ReturnModerators(Moderator(11), Moderator(12));

        var count = await _sut.WarnOverdueSlaAsync();

        Assert.Equal(1, count);
        Assert.NotNull(overdue.SlaWarnedAt);
        _notificationServiceMock.Verify(n => n.CreateAsync(
            11, NotificationType.ModerationSlaWarning, It.Is<string?>(p => p!.Contains("\"itemId\":3")),
            It.IsAny<CancellationToken>()), Times.Once);
        _notificationServiceMock.Verify(n => n.CreateAsync(
            12, NotificationType.ModerationSlaWarning, It.Is<string?>(p => p!.Contains("\"itemId\":3")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WarnOverdueSlaAsync_NoActiveModerator_DoesNotMarkWarnedSoItIsSentLater()
    {
        var overdue = new ModerationItem
        {
            Id = 3, ItemType = ModerationItemType.ContentReport, Status = ModerationItemStatus.Pending,
            SlaDueAt = DateTime.UtcNow.AddHours(-2), SlaWarnedAt = null
        };
        ReturnItems(overdue);
        ReturnModerators();

        var count = await _sut.WarnOverdueSlaAsync();

        Assert.Equal(0, count);
        Assert.Null(overdue.SlaWarnedAt);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
