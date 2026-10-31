using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StoryPlatform.Application.Features.Notifications.DTOs;
using StoryPlatform.Application.Features.Notifications.Interfaces;
using StoryPlatform.Application.Features.StoryWishes.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.UnitTests.Features.ReadingSessions;
using Xunit;

namespace StoryPlatform.UnitTests.Features.StoryWishes;

public class StoryWishNotifierTests
{
    [Fact]
    public async Task Realtime_failure_does_not_escape_and_other_recipients_are_attempted()
    {
        var prepared = new[]
        {
            new StoryPlatform.Application.Features.StoryWishes.PreparedStoryWishNotification(7, new NotificationDto { Id = 1 }),
            new StoryPlatform.Application.Features.StoryWishes.PreparedStoryWishNotification(8, new NotificationDto { Id = 2 })
        };
        _publisher.Setup(x => x.PublishAsync(7, It.IsAny<NotificationDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated transport failure"));
        await _sut.PublishPreparedAsync(prepared);
        _publisher.Verify(x => x.PublishAsync(8, prepared[1].Notification, It.IsAny<CancellationToken>()), Times.Once);
        _notifications.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Cancellation_after_commit_does_not_publish_or_recreate_notifications()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await _sut.PublishPreparedAsync(new[]
        {
            new StoryPlatform.Application.Features.StoryWishes.PreparedStoryWishNotification(7, new NotificationDto { Id = 1 })
        }, cts.Token);
        _publisher.VerifyNoOtherCalls();
        _notifications.VerifyNoOtherCalls();
    }
    private const int Owner = 7;
    private readonly ReadingHarness _h = new();
    private readonly Mock<INotificationService> _notifications = new();
    private readonly Mock<INotificationRealtimePublisher> _publisher = new();
    private readonly List<(int Recipient, NotificationType Type, string? Payload)> _sent = new();
    private readonly StoryWishNotifier _sut;

    public StoryWishNotifierTests()
    {
        _notifications.Setup(n => n.CreateAsync(It.IsAny<int>(), It.IsAny<NotificationType>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback((int recipient, NotificationType type, string? payload, CancellationToken _) =>
                _sent.Add((recipient, type, payload)))
            .ReturnsAsync(new NotificationDto());
        _notifications.Setup(n => n.CreateStoredAsync(
                It.IsAny<int>(), It.IsAny<NotificationType>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int recipient, NotificationType type, string? payload, CancellationToken _) =>
            {
                _sent.Add((recipient, type, payload));
                return new NotificationDto { Id = recipient, Type = type.ToString(), Payload = payload };
            });
        _sut = new StoryWishNotifier(_h.UnitOfWork.Object, _notifications.Object, _publisher.Object, NullLogger<StoryWishNotifier>.Instance);
        _h.AddUser(Owner);
    }

    private static StoryWish Wish() => new()
    {
        Id = 42, ChildProfileId = ReadingHarness.Child, Kind = StoryWishKind.Wish,
        Status = StoryWishStatus.Pending
    };

    [Fact]
    public async Task NotifySubmitted_GoesOnlyToOwner()
    {
        _h.AddUser(8);
        await _sut.NotifySubmittedAsync(Wish());
        Assert.Equal(Owner, Assert.Single(_sent).Recipient);
        Assert.Equal(NotificationType.StoryWishSubmitted, _sent[0].Type);
    }

    [Fact]
    public async Task NotifySubmitted_PayloadCarriesChildWishAndKind()
    {
        await _sut.NotifySubmittedAsync(Wish());
        using var payload = JsonDocument.Parse(Assert.Single(_sent).Payload!);
        Assert.Equal(ReadingHarness.Child, payload.RootElement.GetProperty("childProfileId").GetInt32());
        Assert.Equal(42, payload.RootElement.GetProperty("wishId").GetInt32());
        Assert.Equal("Wish", payload.RootElement.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task NotifySubmitted_SkipsInactiveOwner()
    {
        _h.Users.Single(u => u.Id == Owner).Status = AccountStatus.Suspended;
        await _sut.NotifySubmittedAsync(Wish());
        Assert.Empty(_sent);
    }

    [Fact]
    public async Task NotifyExpiring_GoesToOwnerWithExpiryDate()
    {
        var wish = Wish();
        wish.PendingSince = new DateTime(2026, 10, 16, 3, 0, 0, DateTimeKind.Utc);

        var prepared = await _sut.PrepareExpiringAsync(wish);
        Assert.Single(prepared);
        _publisher.VerifyNoOtherCalls();

        Assert.Equal(Owner, Assert.Single(_sent).Recipient);
        Assert.All(_sent, s => Assert.Equal(NotificationType.StoryWishExpiring, s.Type));
        using var payload = JsonDocument.Parse(_sent.First().Payload!);
        Assert.Equal(42, payload.RootElement.GetProperty("wishId").GetInt32());
        Assert.Equal(new DateTime(2026, 10, 23, 3, 0, 0, DateTimeKind.Utc),
            payload.RootElement.GetProperty("expiresAt").GetDateTime().ToUniversalTime());
    }
}
