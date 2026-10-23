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
    private const int Approver = 8;
    private const int Viewer = 9;
    private const int Revoked = 10;
    private readonly ReadingHarness _h = new();
    private readonly Mock<INotificationService> _notifications = new();
    private readonly List<(int Recipient, NotificationType Type, string? Payload)> _sent = new();
    private readonly StoryWishNotifier _sut;

    public StoryWishNotifierTests()
    {
        _notifications.Setup(n => n.CreateAsync(
                It.IsAny<int>(), It.IsAny<NotificationType>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback((int recipient, NotificationType type, string? payload, CancellationToken _) =>
                _sent.Add((recipient, type, payload)))
            .ReturnsAsync(new NotificationDto());
        _sut = new StoryWishNotifier(_h.UnitOfWork.Object, _notifications.Object);
        foreach (var id in new[] { Owner, Approver, Viewer, Revoked }) _h.AddUser(id);
    }

    private static StoryWish Wish() => new()
    {
        Id = 42,
        ChildProfileId = ReadingHarness.Child,
        Kind = StoryWishKind.Wish,
        Status = StoryWishStatus.Pending
    };

    [Fact]
    public async Task NotifySubmitted_GoesToOwnerAndApproveStoryHolders_Only()
    {
        _h.AddSupervisor(ReadingHarness.Child, Owner, SupervisorRole.Owner);
        _h.AddSupervisor(ReadingHarness.Child, Approver, SupervisorRole.AdditionalSupervisor, null, Permission.ApproveStory);
        _h.AddSupervisor(ReadingHarness.Child, Viewer, SupervisorRole.AdditionalSupervisor, null, Permission.ViewProgress);
        _h.AddSupervisor(ReadingHarness.Child, Revoked, SupervisorRole.AdditionalSupervisor,
            DateTime.UtcNow.AddDays(-1), Permission.ApproveStory);

        await _sut.NotifySubmittedAsync(Wish());

        Assert.Equal(new[] { Owner, Approver }, _sent.Select(s => s.Recipient).OrderBy(id => id));
        Assert.All(_sent, s => Assert.Equal(NotificationType.StoryWishSubmitted, s.Type));
    }

    [Fact]
    public async Task NotifySubmitted_PayloadCarriesChildWishAndKind()
    {
        _h.AddSupervisor(ReadingHarness.Child, Owner, SupervisorRole.Owner);
        await _sut.NotifySubmittedAsync(Wish());
        using var payload = JsonDocument.Parse(_sent.Single().Payload!);
        Assert.Equal(ReadingHarness.Child, payload.RootElement.GetProperty("childProfileId").GetInt32());
        Assert.Equal(42, payload.RootElement.GetProperty("wishId").GetInt32());
        Assert.Equal("Wish", payload.RootElement.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task NotifySubmitted_SkipsInactiveAccounts_AndOtherChildrensSupervisors()
    {
        _h.Users.Single(u => u.Id == Owner).Status = AccountStatus.Suspended;
        _h.AddSupervisor(ReadingHarness.Child, Owner, SupervisorRole.Owner);
        _h.AddSupervisor(ReadingHarness.OtherChild, Approver, SupervisorRole.Owner);
        await _sut.NotifySubmittedAsync(Wish());
        Assert.Empty(_sent);
    }

    [Fact]
    public async Task NotifySubmitted_SamePersonIsNotifiedOnce()
    {
        _h.AddSupervisor(ReadingHarness.Child, Owner, SupervisorRole.Owner, null, Permission.ApproveStory);
        await _sut.NotifySubmittedAsync(Wish());
        Assert.Single(_sent);
    }
}
