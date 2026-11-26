using System.Text.Json;
using Moq;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.Events.Services;
using StoryPlatform.Application.Features.Notifications.DTOs;
using StoryPlatform.Application.Features.Notifications.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.UnitTests.Features.TokenQuota;
using Xunit;

namespace StoryPlatform.UnitTests.Features.Events;

public sealed class EventResultSweepServiceTests
{
    private readonly InMemoryUnitOfWork _uow = new();
    private readonly Mock<IAuditLogWriter> _audit = new();
    private readonly Mock<INotificationService> _notifications = new();
    private readonly DateTime _now = DateTime.UtcNow;

    [Fact]
    public async Task Sweep_NotifiesEndedOnce_WithPersonalRecap()
    {
        var parent = _uow.Seed(new UserAccount { Id = 10, Email = "p@test.local", Role = UserRole.User });
        var child = _uow.Seed(new ChildProfile { Id = 100, OwnerUserId = parent.Id, Nickname = "Bé Bi" });
        var ev = _uow.Seed(new Event
        {
            Id = 1, Title = "Mùa hè đọc sách", Slug = "mua-he", MainCategoryId = 1,
            Status = EventStatus.Published, ConfigVersion = EventConfigVersion.ActivityBased,
            OpensAt = _now.AddDays(-7), ClosesAt = _now.AddHours(-1)
        });
        var readAct = _uow.Seed(new EventActivity
        {
            Id = 20, EventId = 1, ActivityType = EventActivityType.ReadingMissions, AllowedAudience = EventAudience.Parent,
            StartAt = _now.AddDays(-7), EndAt = _now.AddHours(-1), ClaimWindowHours = 72
        });
        _uow.Seed(new EventRuleVersion { Id = 30, EventId = 1, IsActive = true, RulesText = "Rules" });
        var part = _uow.Seed(new EventParticipation { Id = 40, EventId = 1, EventActivityId = readAct.Id, UserId = parent.Id, AcceptedRuleVersionId = 30, Status = EventParticipationStatus.Verified });
        _uow.Seed(new EventStory { Id = 50, EventId = 1, StoryId = 201, StoryVersionId = 301, IsActive = true });
        _uow.Seed(new EventStory { Id = 51, EventId = 1, StoryId = 202, StoryVersionId = 302, IsActive = true });
        _uow.Seed(new ReadingSession { Id = 60, ChildProfileId = child.Id, StoryId = 201, StoryVersionId = 301, Status = SessionStatus.Completed, CompletedAt = _now.AddDays(-3) });
        _uow.Seed(new ReadingSession { Id = 61, ChildProfileId = child.Id, StoryId = 202, StoryVersionId = 302, Status = SessionStatus.Completed, CompletedAt = _now.AddDays(-2) });
        _uow.Seed(new EventVote { Id = 70, EventId = 1, StoryId = 201, StoryVersionId = 301, ParticipationId = part.Id, ParentUserId = parent.Id, Stars = 5 });

        string? capturedPayload = null;
        _notifications
            .Setup(n => n.CreateStoredAsync(parent.Id, NotificationType.EventEnded, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<int, NotificationType, string?, CancellationToken>((_, _, p, _) => capturedPayload = p)
            .ReturnsAsync(new NotificationDto());

        var svc = new EventResultSweepService(_uow, _audit.Object, _notifications.Object);
        var count = await svc.SweepEndedEventsAsync();

        Assert.Equal(1, count);
        Assert.NotNull(ev.EndedNotifiedAt);
        Assert.NotNull(capturedPayload);
        using var doc = JsonDocument.Parse(capturedPayload!);
        Assert.Equal(1, doc.RootElement.GetProperty("eventId").GetInt32());
        Assert.Equal("Mùa hè đọc sách", doc.RootElement.GetProperty("eventTitle").GetString());
        Assert.Equal(2, doc.RootElement.GetProperty("storiesRead").GetInt32());
        Assert.Equal(1, doc.RootElement.GetProperty("votes").GetInt32());
    }

    [Fact]
    public async Task Sweep_NotifiesEndedOnlyOnce()
    {
        var parent = _uow.Seed(new UserAccount { Id = 10, Email = "p@test.local", Role = UserRole.User });
        var ev = _uow.Seed(new Event
        {
            Id = 1, Title = "Sự kiện A", Slug = "sk-a", MainCategoryId = 1,
            Status = EventStatus.Published, ConfigVersion = EventConfigVersion.ActivityBased,
            OpensAt = _now.AddDays(-5), ClosesAt = _now.AddHours(-1)
        });
        var act = _uow.Seed(new EventActivity { Id = 20, EventId = 1, ActivityType = EventActivityType.ReadingMissions, StartAt = _now.AddDays(-5), EndAt = _now.AddHours(-1) });
        _uow.Seed(new EventParticipation { Id = 40, EventId = 1, EventActivityId = act.Id, UserId = parent.Id, Status = EventParticipationStatus.Verified });

        _notifications
            .SetupSequence(n => n.CreateStoredAsync(parent.Id, NotificationType.EventEnded, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Temporary write failure"))
            .ReturnsAsync(new NotificationDto());
        var svc = new EventResultSweepService(_uow, _audit.Object, _notifications.Object);
        Assert.Equal(0, await svc.SweepEndedEventsAsync());
        Assert.Null(ev.EndedNotifiedAt);
        Assert.Contains("rollback", _uow.Calls);
        await svc.SweepEndedEventsAsync();
        await svc.SweepEndedEventsAsync();

        _notifications.Verify(n => n.CreateStoredAsync(parent.Id, NotificationType.EventEnded, It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Theory]
    [InlineData(EventStatus.Published)]
    [InlineData(EventStatus.Paused)]
    public async Task Sweep_ReleasesHold_AfterWindow_WhenNoOpenAppeals(EventStatus status)
    {
        var winner = _uow.Seed(new UserAccount { Id = 10, Email = "w@test.local", Role = UserRole.ContentCreator });
        _uow.Seed(new Event
        {
            Id = 1, Title = "Sáng tác", Slug = "st", MainCategoryId = 1,
            Status = status, ConfigVersion = EventConfigVersion.ActivityBased,
            ClosesAt = _now.AddHours(-60), EndedNotifiedAt = _now.AddHours(-59),
            ResultsFinalizedAt = _now.AddHours(-50), ResultsPublishedAt = _now.AddHours(-49)
        });
        var subAct = _uow.Seed(new EventActivity { Id = 20, EventId = 1, ActivityType = EventActivityType.Submission, StartAt = _now.AddDays(-5), EndAt = _now.AddHours(-60), ClaimWindowHours = 72 });
        var part = _uow.Seed(new EventParticipation { Id = 40, EventId = 1, EventActivityId = subAct.Id, UserId = winner.Id, Status = EventParticipationStatus.Verified });
        var award = _uow.Seed(new EventAward
        {
            Id = 100, EventId = 1, EventActivityId = subAct.Id, ResultSource = EventAwardResultSource.SubmissionScore,
            RankOrTier = "Nhất", SlotIndex = 1, RewardName = "Cúp vàng", ParticipationId = part.Id,
            Status = EventAwardStatus.AppealHold
        });

        var svc = new EventResultSweepService(_uow, _audit.Object, _notifications.Object);
        var count = await svc.SweepEndedEventsAsync();

        Assert.Equal(1, count);
        Assert.Equal(EventAwardStatus.AwaitingClaim, award.Status);
        Assert.NotNull(award.ClaimDeadlineAt);
        Assert.InRange(award.ClaimDeadlineAt!.Value, _now.AddHours(71), _now.AddHours(73));
        _notifications.Verify(n => n.CreateStoredAsync(winner.Id, NotificationType.EventAwardApproved, It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Sweep_KeepsHold_WhileAppealOpen()
    {
        var winner = _uow.Seed(new UserAccount { Id = 10, Email = "w@test.local", Role = UserRole.ContentCreator });
        _uow.Seed(new Event
        {
            Id = 1, Title = "Sáng tác", Slug = "st", MainCategoryId = 1,
            Status = EventStatus.Published, ConfigVersion = EventConfigVersion.ActivityBased,
            ClosesAt = _now.AddHours(-60), EndedNotifiedAt = _now.AddHours(-59),
            ResultsFinalizedAt = _now.AddHours(-50), ResultsPublishedAt = _now.AddHours(-49)
        });
        var subAct = _uow.Seed(new EventActivity { Id = 20, EventId = 1, ActivityType = EventActivityType.Submission, StartAt = _now.AddDays(-5), EndAt = _now.AddHours(-60) });
        var part = _uow.Seed(new EventParticipation { Id = 40, EventId = 1, EventActivityId = subAct.Id, UserId = winner.Id, Status = EventParticipationStatus.Verified });
        var award = _uow.Seed(new EventAward { Id = 100, EventId = 1, EventActivityId = subAct.Id, RankOrTier = "Nhất", SlotIndex = 1, RewardName = "Quà", ParticipationId = part.Id, Status = EventAwardStatus.AppealHold });
        _uow.Seed(new EventAppeal { Id = 900, EventId = 1, ParticipationId = 41, AppellantUserId = 11, ReasonText = "Đang chờ", Status = EventAppealStatus.Open });

        var count = await new EventResultSweepService(_uow, _audit.Object, _notifications.Object).SweepEndedEventsAsync();
        Assert.Equal(0, count);
        Assert.Equal(EventAwardStatus.AppealHold, award.Status);
    }

    [Fact]
    public async Task Sweep_KeepsHold_BeforeWindowEnds()
    {
        var winner = _uow.Seed(new UserAccount { Id = 10, Email = "w@test.local", Role = UserRole.ContentCreator });
        _uow.Seed(new Event
        {
            Id = 1, Title = "Sáng tác", Slug = "st", MainCategoryId = 1,
            Status = EventStatus.Published, ConfigVersion = EventConfigVersion.ActivityBased,
            ClosesAt = _now.AddHours(-5), EndedNotifiedAt = _now.AddHours(-4),
            ResultsFinalizedAt = _now.AddHours(-2), ResultsPublishedAt = _now.AddHours(-1)
        });
        var subAct = _uow.Seed(new EventActivity { Id = 20, EventId = 1, ActivityType = EventActivityType.Submission, StartAt = _now.AddDays(-5), EndAt = _now.AddHours(-5) });
        var part = _uow.Seed(new EventParticipation { Id = 40, EventId = 1, EventActivityId = subAct.Id, UserId = winner.Id, Status = EventParticipationStatus.Verified });
        var award = _uow.Seed(new EventAward { Id = 100, EventId = 1, EventActivityId = subAct.Id, RankOrTier = "Nhất", SlotIndex = 1, RewardName = "Quà", ParticipationId = part.Id, Status = EventAwardStatus.AppealHold });

        var count = await new EventResultSweepService(_uow, _audit.Object, _notifications.Object).SweepEndedEventsAsync();
        Assert.Equal(0, count);
        Assert.Equal(EventAwardStatus.AppealHold, award.Status);
    }
}
