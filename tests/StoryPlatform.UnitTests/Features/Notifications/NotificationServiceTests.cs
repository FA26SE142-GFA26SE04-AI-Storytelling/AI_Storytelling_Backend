using System.Linq.Expressions;
using Moq;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.Notifications.DTOs;
using StoryPlatform.Application.Features.Notifications.Interfaces;
using StoryPlatform.Application.Features.Notifications.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.Notifications;

public class NotificationServiceTests
{
    [Fact]
    public async Task CreateStoredAsync_PersistsWithoutPublishing()
    {
        _repository.Setup(x => x.AddAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Notification row, CancellationToken _) => { row.Id = 42; return row; });
        var result = await _sut.CreateStoredAsync(7, NotificationType.StoryWishExpiring, "{\"childProfileId\":1}");
        Assert.Equal(42, result.Id);
        _unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _publisher.VerifyNoOtherCalls();
    }
    private readonly Mock<IGenericRepository<Notification>> _repository = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<INotificationRealtimePublisher> _publisher = new();
    private readonly NotificationService _sut;

    public NotificationServiceTests()
    {
        _unitOfWork.Setup(work => work.Repository<Notification>()).Returns(_repository.Object);
        _sut = new NotificationService(_unitOfWork.Object, _publisher.Object);
    }

    [Fact]
    public async Task CreateAsync_Valid_SavesAndPublishesRealtime()
    {
        Notification? added = null;
        _repository.Setup(repository => repository.AddAsync(
                It.IsAny<Notification>(), It.IsAny<CancellationToken>()))
            .Callback<Notification, CancellationToken>((notification, _) => added = notification)
            .ReturnsAsync((Notification notification, CancellationToken _) => notification);

        var result = await _sut.CreateAsync(
            7, NotificationType.SupervisionInvite, "{\"childProfileId\":1}");

        Assert.Equal(7, added!.RecipientUserId);
        Assert.Equal(NotificationReadStatus.Unread, added.Status);
        Assert.Equal("SupervisionInvite", result.Type);
        _unitOfWork.Verify(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _publisher.Verify(publisher => publisher.PublishAsync(
            7, It.Is<NotificationDto>(dto => dto.Type == "SupervisionInvite"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_PayloadWithChildProfileId_StoresChildProfileId()
    {
        Notification? added = null;
        _repository.Setup(r => r.AddAsync(
                It.IsAny<Notification>(), It.IsAny<CancellationToken>()))
            .Callback<Notification, CancellationToken>((value, _) => added = value)
            .ReturnsAsync((Notification value, CancellationToken _) => value);

        var dto = await _sut.CreateAsync(
            2, NotificationType.SupervisionRevoked,
            "{\"childProfileId\":7,\"reason\":\"removed\"}");

        Assert.Equal(7, added!.ChildProfileId);
        Assert.Equal(7, dto.ChildProfileId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{\"foo\":1}")]
    [InlineData("{\"childProfileId\":\"abc\"}")]
    [InlineData("not json")]
    public async Task CreateAsync_PayloadWithoutValidChildProfileId_LeavesNull(string? payload)
    {
        Notification? added = null;
        _repository.Setup(r => r.AddAsync(
                It.IsAny<Notification>(), It.IsAny<CancellationToken>()))
            .Callback<Notification, CancellationToken>((value, _) => added = value)
            .ReturnsAsync((Notification value, CancellationToken _) => value);

        await _sut.CreateAsync(2, NotificationType.AccountSecurityChanged, payload);

        Assert.Null(added!.ChildProfileId);
    }

    [Fact]
    public async Task ListAsync_ReturnsOwnNotificationsNewestFirst()
    {
        var older = new Notification { Id = 1, RecipientUserId = 7, CreatedAt = DateTime.UtcNow.AddDays(-1) };
        var newer = new Notification { Id = 2, RecipientUserId = 7, CreatedAt = DateTime.UtcNow };
        _repository.Setup(repository => repository.FindAsync(
                It.IsAny<Expression<Func<Notification, bool>>>(), null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Notification> { older, newer });

        var result = await _sut.ListAsync(7);

        Assert.Equal(new[] { 2, 1 }, result.Select(dto => dto.Id));
    }

    [Fact]
    public async Task ListAsync_WithGroup_QueriesOnlyGroupAndExposesGroup()
    {
        Expression<Func<Notification, bool>>? captured = null;
        _repository.Setup(r => r.FindAsync(
                It.IsAny<Expression<Func<Notification, bool>>>(), null,
                It.IsAny<CancellationToken>()))
            .Callback<Expression<Func<Notification, bool>>, string?, CancellationToken>(
                (predicate, _, _) => captured = predicate)
            .ReturnsAsync(new List<Notification>
            {
                new() { Id = 1, RecipientUserId = 2, Type = NotificationType.SupervisionInvite,
                    CreatedAt = DateTime.UtcNow.AddMinutes(-5) },
                new() { Id = 2, RecipientUserId = 2, Type = NotificationType.PermissionRequest,
                    CreatedAt = DateTime.UtcNow }
            });

        var result = await _sut.ListAsync(2, "supervisors", CancellationToken.None);

        var matches = captured!.Compile();
        Assert.True(matches(new Notification
            { RecipientUserId = 2, Type = NotificationType.SupervisionInvite }));
        Assert.False(matches(new Notification
            { RecipientUserId = 2, Type = NotificationType.AccountSecurityChanged }));
        Assert.False(matches(new Notification
            { RecipientUserId = 3, Type = NotificationType.SupervisionInvite }));
        Assert.Equal(new[] { 2, 1 }, result.Select(n => n.Id));
        Assert.All(result, n => Assert.Equal("supervisors", n.Group));
    }

    [Fact]
    public async Task ListAsync_UnknownGroup_ThrowsBadRequest()
    {
        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.ListAsync(2, "nonsense", CancellationToken.None));
    }

    [Fact]
    public async Task GetUnreadCountAsync_CountsOnlyUnreadOfCurrentUser()
    {
        Expression<Func<Notification, bool>>? captured = null;
        _repository.Setup(r => r.CountAsync(
                It.IsAny<Expression<Func<Notification, bool>>>(),
                It.IsAny<CancellationToken>()))
            .Callback<Expression<Func<Notification, bool>>?, CancellationToken>(
                (predicate, _) => captured = predicate)
            .ReturnsAsync(4);

        var count = await _sut.GetUnreadCountAsync(2);

        Assert.Equal(4, count);
        var matches = captured!.Compile();
        Assert.True(matches(new Notification
            { RecipientUserId = 2, Status = NotificationReadStatus.Unread }));
        Assert.False(matches(new Notification
            { RecipientUserId = 2, Status = NotificationReadStatus.Read }));
        Assert.False(matches(new Notification
            { RecipientUserId = 3, Status = NotificationReadStatus.Unread }));
    }

    [Fact]
    public async Task MarkAsReadAsync_NotOwner_ThrowsNotFound()
    {
        _repository.Setup(repository => repository.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Notification { Id = 1, RecipientUserId = 7 });

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.MarkAsReadAsync(1, 99));
    }

    [Fact]
    public async Task MarkAsReadAsync_Owner_UpdatesAndSaves()
    {
        var notification = new Notification
        {
            Id = 1,
            RecipientUserId = 7,
            Status = NotificationReadStatus.Unread
        };
        _repository.Setup(repository => repository.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(notification);

        await _sut.MarkAsReadAsync(1, 7);

        Assert.Equal(NotificationReadStatus.Read, notification.Status);
        Assert.NotNull(notification.ReadAt);
        _repository.Verify(repository => repository.Update(notification), Times.Once);
        _unitOfWork.Verify(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_NotOwner_ThrowsNotFound()
    {
        _repository.Setup(repository => repository.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Notification { Id = 1, RecipientUserId = 7 });

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.DeleteAsync(1, 99));
    }

    [Fact]
    public async Task DeleteAsync_Owner_DeletesAndSaves()
    {
        var notification = new Notification { Id = 1, RecipientUserId = 7 };
        _repository.Setup(repository => repository.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(notification);

        await _sut.DeleteAsync(1, 7);

        _repository.Verify(repository => repository.Delete(notification), Times.Once);
        _unitOfWork.Verify(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MarkAllAsReadAsync_HasUnreadNotifications_MarksAllReadAndSaves()
    {
        var unread = new List<Notification>
        {
            new() { Id = 1, RecipientUserId = 1, Status = NotificationReadStatus.Unread },
            new() { Id = 2, RecipientUserId = 1, Status = NotificationReadStatus.Unread }
        };
        _repository.Setup(repo => repo.FindAsync(
                It.IsAny<Expression<Func<Notification, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(unread);

        await _sut.MarkAllAsReadAsync(1);

        Assert.All(unread, notification => Assert.Equal(NotificationReadStatus.Read, notification.Status));
        Assert.All(unread, notification => Assert.NotNull(notification.ReadAt));
        _repository.Verify(repo => repo.Update(It.IsAny<Notification>()), Times.Exactly(2));
        _unitOfWork.Verify(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MarkAllAsReadAsync_NoUnreadNotifications_DoesNotCallSaveChanges()
    {
        _repository.Setup(repo => repo.FindAsync(
                It.IsAny<Expression<Func<Notification, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Notification>());

        await _sut.MarkAllAsReadAsync(1);

        _unitOfWork.Verify(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
