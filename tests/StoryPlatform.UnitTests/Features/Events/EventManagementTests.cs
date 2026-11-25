using System.Linq.Expressions;
using Moq;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.Events.DTOs;
using StoryPlatform.Application.Features.Events.Services;
using StoryPlatform.Application.Features.Notifications.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.UnitTests.Features.TokenQuota;
using Xunit;

namespace StoryPlatform.UnitTests.Features.Events;

public class EventManagementTests
{
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IGenericRepository<Event>> _events = new();
    private readonly Mock<IGenericRepository<EventRuleVersion>> _rules = new();
    private readonly Mock<IGenericRepository<EventTaskDefinition>> _tasks = new();
    private readonly Mock<IAuditLogWriter> _audit = new();
    private readonly Event _event = new() { Id = 1, Status = EventStatus.Draft, AssignedModeratorId = 9 };

    public EventManagementTests()
    {
        _uow.Setup(x => x.Repository<Event>()).Returns(_events.Object);
        _uow.Setup(x => x.Repository<EventRuleVersion>()).Returns(_rules.Object);
        _uow.Setup(x => x.Repository<EventTaskDefinition>()).Returns(_tasks.Object);
        _events.Setup(x => x.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(_event);
        _rules.Setup(x => x.FindAsync(It.IsAny<Expression<Func<EventRuleVersion, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<EventRuleVersion>());
    }

    private EventManagementService Service() => new(_uow.Object, _audit.Object);

    [Fact]
    public async Task OtherModeratorCannotChangeRulesOrControlLifecycle()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() => Service().UpsertRuleVersionAsync(10, 1, ValidRule()));
        await Assert.ThrowsAsync<ForbiddenException>(() => Service().SubmitForApprovalAsync(10, 1));
        await Assert.ThrowsAsync<ForbiddenException>(() => Service().PublishEventAsync(10, 1));
        await Assert.ThrowsAsync<ForbiddenException>(() => Service().CloseEventAsync(10, 1));
    }

    [Theory]
    [InlineData(EventStatus.Draft)]
    [InlineData(EventStatus.PendingApproval)]
    [InlineData(EventStatus.Approved)]
    public async Task UnpublishedDetailsAreHiddenFromPublicCallers(EventStatus status)
    {
        _event.Status = status;
        await Assert.ThrowsAsync<NotFoundException>(() => Service().GetEventDetailAsync(1));
    }

    [Fact]
    public async Task StaffCanReadDraftDetails()
    {
        ListRepository<EventTrack>();
        ListRepository<EventAward>();
        ListRepository<EventStory>();
        var result = await Service().GetEventDetailAsync(1, includeUnpublished: true);
        Assert.Equal(EventStatus.Draft, result.Status);
    }

    private Mock<IGenericRepository<T>> ListRepository<T>(params T[] items) where T : class
    {
        var repo = new Mock<IGenericRepository<T>>();
        repo.Setup(x => x.FindAsync(It.IsAny<Expression<Func<T, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<T, bool>> predicate, string? _, CancellationToken _) => items.Where(predicate.Compile()).ToArray());
        _uow.Setup(x => x.Repository<T>()).Returns(repo.Object);
        return repo;
    }

    private static UpsertEventRuleRequestDto ValidRule() => new()
    {
        RulesText = "Thể lệ",
        Tasks = [new() { Title = "Đọc truyện", VerifierType = TaskVerifierType.ReadEventStories, TargetValue = 2 }]
    };

    [Theory]
    [InlineData(EventStatus.Approved)]
    [InlineData(EventStatus.Published)]
    [InlineData(EventStatus.Closed)]
    public async Task RulesCannotChangeAfterApproval(EventStatus status)
    {
        _event.Status = status;
        await Assert.ThrowsAsync<ConflictException>(() => Service().UpsertRuleVersionAsync(9, 1, ValidRule()));
        _uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("target")]
    [InlineData("verifier")]
    [InlineData("json")]
    [InlineData("duplicate")]
    [InlineData("points")]
    [InlineData("count")]
    public async Task InvalidRulesAreRejectedBeforeAnySave(string invalid)
    {
        var request = ValidRule();
        switch (invalid)
        {
            case "target": request.Tasks[0].TargetValue = 0; break;
            case "verifier": request.Tasks[0].VerifierType = (TaskVerifierType)999; break;
            case "json": request.Tasks[0].ConfigJson = "{broken"; break;
            case "duplicate": request.Tasks.Add(new() { TaskCode = "T01", Title = "Trùng mã tự sinh", TargetValue = 1, VerifierType = TaskVerifierType.ReadEventStories }); break;
            case "points": request.CompletionMode = TrackCompletionMode.MinimumPointsThreshold; request.RequiredPoints = 999; break;
            case "count": request.CompletionMode = TrackCompletionMode.MinimumTasksCount; request.RequiredTaskCount = 2; break;
        }
        await Assert.ThrowsAsync<BadRequestException>(() => Service().UpsertRuleVersionAsync(9, 1, request));
        _uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(x => x.RollbackTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FailureSavingTasksRollsBackTheNewRuleAndOldActivation()
    {
        var old = new EventRuleVersion { Id = 8, EventId = 1, VersionNo = 1, IsActive = true };
        _rules.Setup(x => x.FindAsync(It.IsAny<Expression<Func<EventRuleVersion, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { old });
        _uow.SetupSequence(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1).ThrowsAsync(new InvalidOperationException("write failed"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service().UpsertRuleVersionAsync(9, 1, ValidRule()));
        _uow.Verify(x => x.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(x => x.RollbackTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(x => x.DiscardTrackedChanges(), Times.Once);
    }

    [Fact]
    public async Task RuleChangesReturnPendingApprovalToDraft()
    {
        _event.Status = EventStatus.PendingApproval;
        var response = await Service().UpsertRuleVersionAsync(9, 1, ValidRule());
        Assert.Equal(EventStatus.Draft, _event.Status);
        Assert.Single(response.Tasks);
        _uow.Verify(x => x.AcquireEventLockAsync(1, It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(x => x.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdatePersistsTrackScheduleAndReplacesDraftAwards()
    {
        _event.Status = EventStatus.PendingApproval;
        var track = new EventTrack { Id = 2, EventId = 1, TrackType = EventTrackType.Parent };
        var tracks = ListRepository(track);
        var awards = ListRepository<EventAward>();
        ListRepository<EventStory>();
        var categories = ListRepository<ContentCategory>();
        categories.Setup(x => x.ExistsAsync(It.IsAny<Expression<Func<ContentCategory, bool>>>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var start = DateTime.UtcNow.AddDays(1);
        await Service().UpdateEventAsync(9, 1, new()
        {
            Title = "Updated", MainCategoryId = 1, BudgetVnd = 100,
            Tracks = [new() { TrackType = EventTrackType.Parent, RegistrationStartAt = start,
                RegistrationEndAt = start.AddDays(1), ExecutionStartAt = start, ExecutionEndAt = start.AddDays(5),
                GrantAiCredits = 7 }],
            AwardSlots = [new() { TrackType = EventTrackType.Parent, RankOrTier = "A", RewardName = "Gift", UnitValueVnd = 100 }]
        });
        Assert.Equal(EventStatus.Draft, _event.Status);
        Assert.Equal(start, track.RegistrationStartAt);
        Assert.Equal(7, track.GrantAiCredits);
        tracks.Verify(x => x.Update(track), Times.Once);
        awards.Verify(x => x.AddAsync(It.Is<EventAward>(a => a.TrackId == 2 && a.UnitValueVnd == 100), It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(x => x.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(TaskVerifierType.ReadEventStories)]
    [InlineData(TaskVerifierType.CompleteStoryQuiz)]
    [InlineData(TaskVerifierType.SaveStoryVocabulary)]
    public async Task PublishRejectsParentTrackWithoutEnoughEligibleStories(TaskVerifierType verifier)
    {
        _event.Status = EventStatus.Approved;
        var tracks = ListRepository<EventTrack>();
        tracks.Setup(x => x.FirstOrDefaultAsync(It.IsAny<Expression<Func<EventTrack, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EventTrack { EventId = 1, TrackType = EventTrackType.Parent, RegistrationStartAt = DateTime.UtcNow.AddDays(-1) });
        _rules.Setup(x => x.FirstOrDefaultAsync(It.IsAny<Expression<Func<EventRuleVersion, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EventRuleVersion { Id = 5, EventId = 1, IsActive = true });
        ListRepository(new EventTaskDefinition { EventRuleVersionId = 5, VerifierType = verifier, TargetValue = 2 });
        ListRepository(new EventStory { EventId = 1, IsActive = true, StoryId = 7 });
        ListRepository(new Story { Id = 7, IsPublished = true, OutputMode = StoryOutputMode.ReadingMediaOnly });
        await Assert.ThrowsAsync<ConflictException>(() => Service().PublishEventAsync(9, 1));
        Assert.Equal(EventStatus.Approved, _event.Status);
        _uow.Verify(x => x.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CloseEvent_WithFailedAward_ThrowsConflict()
    {
        var store = new InMemoryUnitOfWork();
        var now = DateTime.UtcNow;
        store.Seed(new Event { Id = 1, Status = EventStatus.Published, ConfigVersion = EventConfigVersion.ActivityBased, AssignedModeratorId = 9, ResultsFinalizedAt = now.AddMinutes(-5) });
        store.Seed(new EventActivity { Id = 2, EventId = 1, ActivityType = EventActivityType.ReadingMissions, EndAt = now.AddMinutes(-10) });
        store.Seed(new EventParticipation { Id = 3, EventId = 1, EventActivityId = 2, UserId = 50, Status = EventParticipationStatus.Verified });
        store.Seed(new EventAward { Id = 4, EventId = 1, EventActivityId = 2, ParticipationId = 3, Status = EventAwardStatus.Failed });

        var ex = await Assert.ThrowsAsync<ConflictException>(() => new EventManagementService(store, _audit.Object).CloseEventAsync(9, 1));
        Assert.Contains("chưa hoàn tất", ex.Message);
    }

    [Fact]
    public async Task CloseEvent_NonVotingWithUnassignedAwards_ThrowsConflict()
    {
        var store = new InMemoryUnitOfWork();
        var now = DateTime.UtcNow;
        store.Seed(new Event { Id = 1, Status = EventStatus.Published, ConfigVersion = EventConfigVersion.ActivityBased, AssignedModeratorId = 9 });
        store.Seed(new EventActivity { Id = 2, EventId = 1, ActivityType = EventActivityType.ReadingMissions, EndAt = now.AddMinutes(-10) });
        store.Seed(new EventAward { Id = 4, EventId = 1, EventActivityId = 2, ParticipationId = null, Status = EventAwardStatus.AwardProposed });

        var ex = await Assert.ThrowsAsync<ConflictException>(() => new EventManagementService(store, _audit.Object).CloseEventAsync(9, 1));
        Assert.Equal("Cần chốt và duyệt danh sách trao giải trước khi đóng sự kiện.", ex.Message);
    }

    [Fact]
    public async Task CloseEvent_NoAwardsConfigured_Succeeds()
    {
        var store = new InMemoryUnitOfWork();
        var now = DateTime.UtcNow;
        store.Seed(new Event { Id = 1, Status = EventStatus.Published, ConfigVersion = EventConfigVersion.ActivityBased, AssignedModeratorId = 9 });
        store.Seed(new EventActivity { Id = 2, EventId = 1, ActivityType = EventActivityType.ReadingMissions, EndAt = now.AddMinutes(-10) });

        var summary = await new EventManagementService(store, _audit.Object).CloseEventAsync(9, 1);

        Assert.Equal(EventStatus.Closed, summary.Status);
    }

    [Fact]
    public async Task CloseEvent_AfterApproveWithNoEligibleWinners_Succeeds()
    {
        var store = new InMemoryUnitOfWork();
        var now = DateTime.UtcNow;
        store.Seed(new Event { Id = 1, Status = EventStatus.Published, ConfigVersion = EventConfigVersion.LegacyRoleBased, ScoringMode = EventScoringMode.Rubric, AssignedModeratorId = 9 });
        store.Seed(new EventTrack { Id = 2, EventId = 1, TrackType = EventTrackType.Creator, ExecutionEndAt = now.AddMinutes(-10) });
        store.Seed(new EventAward { Id = 4, EventId = 1, TrackId = 2, Status = EventAwardStatus.AwardProposed });

        await new EventRewardService(store, _audit.Object).ApproveAwardsAsync(10, 1);
        var summary = await new EventManagementService(store, _audit.Object).CloseEventAsync(9, 1);

        Assert.Equal(EventStatus.Closed, summary.Status);
    }

    [Fact]
    public async Task SafetyWarningCategory_CannotBeTheEventTopic_OnCreateOrUpdate()
    {
        var store = new InMemoryUnitOfWork();
        store.Seed(new ContentCategory { Id = 1, IsActive = true });
        store.Seed(new ContentCategory { Id = 7, IsActive = true, Group = ContentCategoryGroup.SafetyWarning });
        var now = DateTime.UtcNow;
        CreateEventRequestDto Request(int categoryId) => new()
        {
            Title = "Chủ đề", Description = "Mô tả", MainCategoryId = categoryId, ConfigVersion = EventConfigVersion.ActivityBased,
            OpensAt = now, ClosesAt = now.AddDays(3),
            Activities = [new UpsertEventActivityDto { ActivityType = EventActivityType.Submission, AllowedAudience = EventAudience.Creator, RegistrationStartAt = now, RegistrationEndAt = now.AddDays(1), StartAt = now, EndAt = now.AddDays(2) }]
        };
        var service = new EventManagementService(store, _audit.Object);

        var created = await service.CreateEventAsync(9, Request(1));
        var onCreate = await Assert.ThrowsAsync<BadRequestException>(() => service.CreateEventAsync(9, Request(7)));
        var onUpdate = await Assert.ThrowsAsync<BadRequestException>(() => service.UpdateEventAsync(9, created.Id, Request(7)));

        Assert.Equal("Danh mục chính của sự kiện không hợp lệ.", onCreate.Message);
        Assert.Equal(onCreate.Message, onUpdate.Message);
    }

    [Fact]
    public async Task ActivityAward_PlatformCreditsZero_ThrowsBadRequest()
    {
        var store = new InMemoryUnitOfWork();
        store.Seed(new ContentCategory { Id = 1, IsActive = true });
        var now = DateTime.UtcNow;
        var ex = await Assert.ThrowsAsync<BadRequestException>(() => new EventManagementService(store, _audit.Object).CreateEventAsync(9, new CreateEventRequestDto
        {
            Title = "Credits 0", MainCategoryId = 1, ConfigVersion = EventConfigVersion.ActivityBased,
            Activities = [new UpsertEventActivityDto { ActivityType = EventActivityType.Submission, AllowedAudience = EventAudience.Creator, RegistrationStartAt = now, RegistrationEndAt = now.AddDays(1), StartAt = now, EndAt = now.AddDays(2) }],
            AwardSlots = [new UpsertEventAwardSlotDto { ActivityType = EventActivityType.Submission, ResultSource = EventAwardResultSource.SubmissionScore, TargetAudience = EventAudience.Creator, RankOrTier = "Giải", RewardType = EventRewardType.PlatformCredits, RewardName = "Credit", CreditGrantAmount = 0 }]
        }));
        Assert.Equal("Giải tín dụng phải cấp ít nhất 1 lượt.", ex.Message);
    }

    [Fact]
    public async Task LegacyAward_PlatformCreditsZero_ThrowsBadRequest()
    {
        var store = new InMemoryUnitOfWork();
        store.Seed(new ContentCategory { Id = 1, IsActive = true });
        var now = DateTime.UtcNow;
        var ex = await Assert.ThrowsAsync<BadRequestException>(() => new EventManagementService(store, _audit.Object).CreateEventAsync(9, new CreateEventRequestDto
        {
            Title = "Legacy Credits 0", MainCategoryId = 1, ConfigVersion = EventConfigVersion.LegacyRoleBased,
            Tracks = [new UpsertEventTrackDto { TrackType = EventTrackType.Parent, RegistrationStartAt = now, RegistrationEndAt = now.AddDays(1), ExecutionStartAt = now, ExecutionEndAt = now.AddDays(2) }],
            AwardSlots = [new UpsertEventAwardSlotDto { TrackType = EventTrackType.Parent, RankOrTier = "Giải", RewardType = EventRewardType.PlatformCredits, RewardName = "Credit", CreditGrantAmount = 0 }]
        }));
        Assert.Equal("Giải tín dụng phải cấp ít nhất 1 lượt.", ex.Message);
    }

    [Fact]
    public async Task ApproveAwards_SendsEventAwardApprovedNotification()
    {
        var store = new InMemoryUnitOfWork();
        var notifications = new Mock<INotificationService>();
        var now = DateTime.UtcNow;
        store.Seed(new Event { Id = 1, Status = EventStatus.Published, ConfigVersion = EventConfigVersion.ActivityBased, AssignedModeratorId = 9, ResultsFinalizedAt = now.AddMinutes(-1) });
        store.Seed(new EventActivity { Id = 2, EventId = 1, ActivityType = EventActivityType.ReadingMissions, EndAt = now.AddMinutes(-10), ClaimWindowHours = 48 });
        store.Seed(new EventParticipation { Id = 3, EventId = 1, EventActivityId = 2, UserId = 55, Status = EventParticipationStatus.Verified });
        store.Seed(new EventAward { Id = 4, EventId = 1, EventActivityId = 2, ParticipationId = 3, RankOrTier = "Nhất", RewardName = "Cúp", Status = EventAwardStatus.AwardProposed });

        await new EventRewardService(store, _audit.Object, notifications: notifications.Object).ApproveAwardsAsync(10, 1);

        notifications.Verify(n => n.CreateAsync(55, NotificationType.EventAwardApproved, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ApproveAwards_NotificationFailureDoesNotUndoApproval_AndCanResend()
    {
        var store = new InMemoryUnitOfWork();
        var notifications = new Mock<INotificationService>();
        var now = DateTime.UtcNow;
        store.Seed(new Event { Id = 1, Status = EventStatus.Published, AssignedModeratorId = 9, ResultsFinalizedAt = now });
        store.Seed(new EventTrack { Id = 2, EventId = 1, ExecutionEndAt = now.AddHours(-1) });
        store.Seed(new EventParticipation { Id = 3, EventId = 1, TrackId = 2, UserId = 55, Status = EventParticipationStatus.Verified });
        store.Seed(new EventAward { Id = 4, EventId = 1, TrackId = 2, ParticipationId = 3, Status = EventAwardStatus.AwardProposed });
        store.Seed(new EventAward { Id = 5, EventId = 1, TrackId = 2, ParticipationId = 3, Status = EventAwardStatus.Fulfilled });
        var setup = notifications.SetupSequence(n => n.CreateAsync(55, NotificationType.EventAwardApproved,
            It.IsAny<string>(), It.IsAny<CancellationToken>()));
        setup.ThrowsAsync(new InvalidOperationException()).ReturnsAsync(new StoryPlatform.Application.Features.Notifications.DTOs.NotificationDto());
        var service = new EventRewardService(store, _audit.Object, notifications.Object);

        var award = Assert.Single(await service.ApproveAwardsAsync(10, 1));
        Assert.True(award.NotificationFailed);
        Assert.Equal(EventAwardStatus.AwaitingClaim, award.Status);
        _audit.Verify(a => a.LogAsync(10, "EventAwardNotificationFailed", nameof(EventAward), 4,
            null, It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
        await Assert.ThrowsAsync<ForbiddenException>(() => service.ResendAwardNotificationsAsync(99, 1));
        var retry = await service.ResendAwardNotificationsAsync(9, 1);
        Assert.Equal(1, retry.SentCount);
        Assert.Empty(retry.FailedAwardIds);
    }
}
