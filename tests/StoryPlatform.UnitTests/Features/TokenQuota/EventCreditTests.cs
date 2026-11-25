using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.TokenQuota.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.TokenQuota;

public class EventCreditTests
{
    private readonly InMemoryUnitOfWork _store = new();
    private readonly TokenQuotaService _service;
    private readonly Story _story;
    private readonly CreditLot _ai;
    private readonly CreditLot _submit;

    public EventCreditTests()
    {
        _store.Seed(new UserAccount { Id = 50, Role = UserRole.ContentCreator, Status = AccountStatus.Active });
        _store.Seed(new Event { Id = 1, Status = EventStatus.Published });
        _store.Seed(new EventTrack { Id = 2, EventId = 1, TrackType = EventTrackType.Creator,
            ExecutionStartAt = DateTime.UtcNow.AddDays(-1), ExecutionEndAt = DateTime.UtcNow.AddDays(2) });
        _store.Seed(new EventParticipation { EventId = 1, TrackId = 2, UserId = 50, Status = EventParticipationStatus.Registered });
        _story = _store.Seed(new Story { Id = 7, AuthorUserId = 50, Scope = StoryScope.Creator, Source = StorySource.Ai });
        _ai = Grant(CreditType.AI, 1);
        _submit = Grant(CreditType.Submit, 1);
        _service = new TokenQuotaService(_store);
    }

    private CreditLot Grant(CreditType type, int eventId) => _store.Seed(new CreditLot
    {
        UserId = 50, Source = CreditSource.EventGrant, CreditType = type, EventId = eventId,
        Total = 3, Remaining = 3, ExpiresAt = DateTime.UtcNow.AddMinutes(10)
    });

    [Fact]
    public async Task EventRequestCanUseMatchingGrantAndRefundReturnsToSameLot()
    {
        var other = Grant(CreditType.AI, 99);
        await _service.EnsureCreatorEventWithinQuotaAsync(50, 1);
        await _service.IncrementCreatorEventUsageAsync(50, _story, 1);
        Assert.Equal(2, _ai.Remaining);
        Assert.Equal(3, other.Remaining);
        Assert.Equal(3, _submit.Remaining);
        await _service.ReleaseStoryCreditAsync(7);
        Assert.Equal(3, _ai.Remaining);
    }

    [Fact]
    public async Task OrdinaryStoryCannotUseEventGrants()
    {
        await _service.IncrementCreatorUsageAsync(50, _story);
        Assert.Equal(3, _ai.Remaining);
        Assert.NotEqual(_ai.Id, _store.Items<CreditUsage>().Single().LotId);
    }

    [Fact]
    public async Task UnregisteredEventAndClosedEventCannotConsumeGrants()
    {
        await Assert.ThrowsAsync<ConflictException>(() => _service.EnsureCreatorEventWithinQuotaAsync(50, 99));
        _store.Items<Event>().Single().Status = EventStatus.Closed;
        await Assert.ThrowsAsync<ConflictException>(() => _service.IncrementCreatorEventUsageAsync(50, _story, 1));
        Assert.Equal(3, _ai.Remaining);
    }

    [Fact]
    public async Task ParentRetryUsesTheGenerationEventWithoutOrdinaryCredits()
    {
        const int parentId = 75;
        const int childId = 76;
        const int storyId = 77;
        const int eventId = 78;
        const int activityId = 79;
        _store.Seed(new UserAccount { Id = parentId, Role = UserRole.User, Status = AccountStatus.Active });
        _store.Seed(new ChildProfile { Id = childId, OwnerUserId = parentId, Status = ChildProfileStatus.Active });
        _store.Seed(new Story { Id = storyId, AuthorUserId = parentId, ChildProfileId = childId, Scope = StoryScope.Parent, Source = StorySource.Ai });
        _store.Seed(new Event { Id = eventId, Status = EventStatus.Published, ConfigVersion = EventConfigVersion.ActivityBased });
        _store.Seed(new EventActivity { Id = activityId, EventId = eventId, ActivityType = EventActivityType.Submission,
            AllowedAudience = EventAudience.Parent, StartAt = DateTime.UtcNow.AddDays(-1), EndAt = DateTime.UtcNow.AddDays(1) });
        _store.Seed(new EventParticipation { EventId = eventId, EventActivityId = activityId, UserId = parentId,
            Status = EventParticipationStatus.Registered });
        var original = _store.Seed(new StoryGenerationRequest { StoryId = storyId, ContextSnapshotJson = "{\"eventId\":78}" });
        _store.Seed(new StoryGenerationRequest { StoryId = storyId, ContextSnapshotJson = "{}" });
        var lot = _store.Seed(new CreditLot { UserId = parentId, Source = CreditSource.EventGrant, CreditType = CreditType.AI,
            EventId = eventId, Total = 2, Remaining = 2, ExpiresAt = DateTime.UtcNow.AddHours(1) });
        var story = _store.Items<Story>().Single(s => s.Id == storyId);
        await _service.IncrementParentEventUsageAsync(parentId, childId, story, eventId);
        await _service.ReleaseStoryCreditAsync(storyId);
        foreach (var ordinary in _store.Items<CreditLot>().Where(x => x.UserId == parentId && x.Source != CreditSource.EventGrant))
            ordinary.Remaining = 0;

        Assert.True(await _service.HoldStoryCreditForRetryAsync(storyId, requestId: original.Id));
        Assert.Equal(1, lot.Remaining);
        Assert.Equal(lot.Id, Assert.Single(_store.Items<CreditUsage>().Where(x => x.StoryId == storyId && x.RefundedAt == null)).LotId);
    }

    [Fact]
    public async Task RetryUsesPinnedEventAndSubmissionUsesBoundEvent()
    {
        _store.Seed(new StoryGenerationRequest { StoryId = 7, ContextSnapshotJson = "{\"eventId\":1}" });
        await _service.IncrementCreatorEventUsageAsync(50, _story, 1);
        await _service.ReleaseStoryCreditAsync(7);
        Assert.True(await _service.HoldStoryCreditForRetryAsync(7));
        Assert.Equal(2, _ai.Remaining);
        _store.Seed(new EventSubmission { EventId = 1, StoryId = 7, CreatorUserId = 50 });
        _store.Seed(new ModerationItem { Id = 8, StoryId = 7 });
        await _service.ChargeSubmissionAsync(50, 8, null, eventId: 1);
        Assert.Equal(2, _submit.Remaining);
        await _service.RefundSubmissionAsync(8);
        Assert.Equal(3, _submit.Remaining);
    }

    [Fact]
    public async Task RetryParentEventStory_AfterEventClosed_UsesOrdinaryCredit()
    {
        const int parentId = 81;
        const int childId = 82;
        const int storyId = 83;
        const int eventId = 84;
        const int activityId = 85;
        _store.Seed(new UserAccount { Id = parentId, Role = UserRole.User, Status = AccountStatus.Active });
        _store.Seed(new ChildProfile { Id = childId, OwnerUserId = parentId, Status = ChildProfileStatus.Active });
        var story = _store.Seed(new Story { Id = storyId, AuthorUserId = parentId, ChildProfileId = childId, Scope = StoryScope.Parent, Source = StorySource.Ai });
        _store.Seed(new Event { Id = eventId, Status = EventStatus.Published, ConfigVersion = EventConfigVersion.ActivityBased });
        var activity = _store.Seed(new EventActivity
        {
            Id = activityId,
            EventId = eventId,
            ActivityType = EventActivityType.Submission,
            AllowedAudience = EventAudience.Parent,
            StartAt = DateTime.UtcNow.AddDays(-2),
            EndAt = DateTime.UtcNow.AddDays(1),
        });
        _store.Seed(new EventParticipation { EventId = eventId, EventActivityId = activityId, UserId = parentId, Status = EventParticipationStatus.Registered });
        var req = _store.Seed(new StoryGenerationRequest { StoryId = storyId, ContextSnapshotJson = $"{{\"eventId\":{eventId}}}" });
        var eventLot = _store.Seed(new CreditLot
        {
            UserId = parentId,
            Source = CreditSource.EventGrant,
            CreditType = CreditType.AI,
            EventId = eventId,
            Total = 1,
            Remaining = 1,
            ExpiresAt = DateTime.UtcNow.AddHours(1),
        });
        var ordinaryLot = _store.Seed(new CreditLot
        {
            UserId = parentId,
            Source = CreditSource.AddOn,
            CreditType = CreditType.AI,
            Total = 1,
            Remaining = 1,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
        });

        await _service.IncrementParentEventUsageAsync(parentId, childId, story, eventId);
        await _service.ReleaseStoryCreditAsync(storyId);
        activity.EndAt = DateTime.UtcNow.AddMinutes(-1);

        Assert.True(await _service.HoldStoryCreditForRetryAsync(storyId, requestId: req.Id));
        Assert.Equal(0, ordinaryLot.Remaining);
        Assert.Equal(1, eventLot.Remaining);
    }

    [Fact]
    public async Task RetryParentEventStory_EventOpen_UsesEventGrantFirst()
    {
        const int parentId = 86;
        const int childId = 87;
        const int storyId = 88;
        const int eventId = 89;
        const int activityId = 90;
        _store.Seed(new UserAccount { Id = parentId, Role = UserRole.User, Status = AccountStatus.Active });
        _store.Seed(new ChildProfile { Id = childId, OwnerUserId = parentId, Status = ChildProfileStatus.Active });
        var story = _store.Seed(new Story { Id = storyId, AuthorUserId = parentId, ChildProfileId = childId, Scope = StoryScope.Parent, Source = StorySource.Ai });
        _store.Seed(new Event { Id = eventId, Status = EventStatus.Published, ConfigVersion = EventConfigVersion.ActivityBased });
        _store.Seed(new EventActivity
        {
            Id = activityId,
            EventId = eventId,
            ActivityType = EventActivityType.Submission,
            AllowedAudience = EventAudience.Parent,
            StartAt = DateTime.UtcNow.AddDays(-1),
            EndAt = DateTime.UtcNow.AddDays(1),
        });
        _store.Seed(new EventParticipation { EventId = eventId, EventActivityId = activityId, UserId = parentId, Status = EventParticipationStatus.Registered });
        var req = _store.Seed(new StoryGenerationRequest { StoryId = storyId, ContextSnapshotJson = $"{{\"eventId\":{eventId}}}" });
        var eventLot = _store.Seed(new CreditLot
        {
            UserId = parentId,
            Source = CreditSource.EventGrant,
            CreditType = CreditType.AI,
            EventId = eventId,
            Total = 1,
            Remaining = 1,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
        });
        var ordinaryLot = _store.Seed(new CreditLot
        {
            UserId = parentId,
            Source = CreditSource.AddOn,
            CreditType = CreditType.AI,
            Total = 1,
            Remaining = 1,
            ExpiresAt = DateTime.UtcNow.AddHours(1),
        });

        await _service.IncrementParentEventUsageAsync(parentId, childId, story, eventId);
        await _service.ReleaseStoryCreditAsync(storyId);

        Assert.True(await _service.HoldStoryCreditForRetryAsync(storyId, requestId: req.Id));
        Assert.Equal(0, eventLot.Remaining);
        Assert.Equal(1, ordinaryLot.Remaining);
    }

    [Fact]
    public async Task CreateParentEventStory_AfterEventClosed_StillThrows()
    {
        const int parentId = 91;
        const int childId = 92;
        const int storyId = 93;
        const int eventId = 94;
        const int activityId = 95;
        _store.Seed(new UserAccount { Id = parentId, Role = UserRole.User, Status = AccountStatus.Active });
        _store.Seed(new ChildProfile { Id = childId, OwnerUserId = parentId, Status = ChildProfileStatus.Active });
        var story = _store.Seed(new Story { Id = storyId, AuthorUserId = parentId, ChildProfileId = childId, Scope = StoryScope.Parent, Source = StorySource.Ai });
        _store.Seed(new Event { Id = eventId, Status = EventStatus.Published, ConfigVersion = EventConfigVersion.ActivityBased });
        _store.Seed(new EventActivity
        {
            Id = activityId,
            EventId = eventId,
            ActivityType = EventActivityType.Submission,
            AllowedAudience = EventAudience.Parent,
            StartAt = DateTime.UtcNow.AddDays(-2),
            EndAt = DateTime.UtcNow.AddMinutes(-1),
        });
        _store.Seed(new EventParticipation { EventId = eventId, EventActivityId = activityId, UserId = parentId, Status = EventParticipationStatus.Registered });
        _store.Seed(new CreditLot
        {
            UserId = parentId,
            Source = CreditSource.AddOn,
            CreditType = CreditType.AI,
            Total = 1,
            Remaining = 1,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
        });

        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.IncrementParentEventUsageAsync(parentId, childId, story, eventId));
    }

    [Fact]
    public async Task ChargeSubmission_PlainSubmit_DoesNotUseEventGrant()
    {
        _store.Seed(new EventSubmission { EventId = 1, StoryId = 7, CreatorUserId = 50, Status = EventSubmissionStatus.PendingReview });
        _store.Seed(new ModerationItem { Id = 801, StoryId = 7 });
        await _service.GetCreatorQuotaAsync(50);
        var monthly = _store.Items<CreditLot>().Single(x =>
            x.UserId == 50 && x.Source == CreditSource.CreatorMonthly && x.CreditType == CreditType.Submit);
        var before = monthly.Remaining;

        await _service.ChargeSubmissionAsync(50, 801, null);

        Assert.Equal(before - 1, monthly.Remaining);
        Assert.Equal(3, _submit.Remaining);
    }

    [Fact]
    public async Task ChargeSubmission_ForEvent_UsesEventGrantFirst()
    {
        _store.Seed(new ModerationItem { Id = 802, StoryId = 7 });
        await _service.GetCreatorQuotaAsync(50);
        var monthly = _store.Items<CreditLot>().Single(x =>
            x.UserId == 50 && x.Source == CreditSource.CreatorMonthly && x.CreditType == CreditType.Submit);
        var before = monthly.Remaining;

        await _service.ChargeSubmissionAsync(50, 802, null, eventId: 1);

        Assert.Equal(2, _submit.Remaining);
        Assert.Equal(before, monthly.Remaining);
    }

    [Fact]
    public async Task ChargeSubmission_ForEvent_NoEventGrantLeft_UsesMonthly()
    {
        _submit.Remaining = 0;
        _store.Seed(new ModerationItem { Id = 803, StoryId = 7 });
        await _service.GetCreatorQuotaAsync(50);
        var monthly = _store.Items<CreditLot>().Single(x =>
            x.UserId == 50 && x.Source == CreditSource.CreatorMonthly && x.CreditType == CreditType.Submit);
        var before = monthly.Remaining;

        await _service.ChargeSubmissionAsync(50, 803, null, eventId: 1);

        Assert.Equal(before - 1, monthly.Remaining);
        Assert.Equal(0, _submit.Remaining);
    }
}
