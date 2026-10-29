using System.Text.Json;
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
    private const int Owner = 7;
    private readonly ReadingHarness _h = new();
    private readonly Mock<INotificationService> _notifications = new();
    private readonly List<(int Recipient, NotificationType Type, string? Payload)> _sent = new();
    private readonly StoryWishNotifier _sut;

    public StoryWishNotifierTests()
    {
        _notifications.Setup(n => n.CreateAsync(It.IsAny<int>(), It.IsAny<NotificationType>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback((int recipient, NotificationType type, string? payload, CancellationToken _) =>
                _sent.Add((recipient, type, payload)))
            .ReturnsAsync(new NotificationDto());
        _sut = new StoryWishNotifier(_h.UnitOfWork.Object, _notifications.Object);
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
        await _sut.NotifyExpiringAsync(wish);
        Assert.Equal(Owner, Assert.Single(_sent).Recipient);
        Assert.Equal(NotificationType.StoryWishExpiring, _sent[0].Type);
        using var payload = JsonDocument.Parse(_sent[0].Payload!);
        Assert.Equal(new DateTime(2026, 10, 23, 3, 0, 0, DateTimeKind.Utc),
            payload.RootElement.GetProperty("expiresAt").GetDateTime().ToUniversalTime());
    }
}
