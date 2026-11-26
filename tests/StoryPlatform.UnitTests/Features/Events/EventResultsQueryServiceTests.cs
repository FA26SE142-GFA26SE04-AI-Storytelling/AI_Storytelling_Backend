using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.Events.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.UnitTests.Features.TokenQuota;
using Xunit;

namespace StoryPlatform.UnitTests.Features.Events;

public sealed class EventResultsQueryServiceTests
{
    private readonly InMemoryUnitOfWork _uow = new();
    private readonly DateTime _now = DateTime.UtcNow;

    private (Event Ev, EventActivity SubAct, EventActivity ReadAct) SeedEndedEvent(bool published)
    {
        var ev = _uow.Seed(new Event
        {
            Id = 1, Title = "Lễ hội Mùa Thu", Slug = "mua-thu", MainCategoryId = 1,
            Status = EventStatus.Published, ConfigVersion = EventConfigVersion.ActivityBased,
            OpensAt = _now.AddDays(-7), ClosesAt = _now.AddHours(-3),
            ResultsFinalizedAt = published ? _now.AddHours(-2) : null,
            ResultsPublishedAt = published ? _now.AddHours(-1) : null
        });
        var subAct = _uow.Seed(new EventActivity { Id = 20, EventId = 1, ActivityType = EventActivityType.Submission, AllowedAudience = EventAudience.Creator, StartAt = _now.AddDays(-7), EndAt = _now.AddHours(-3) });
        var readAct = _uow.Seed(new EventActivity { Id = 21, EventId = 1, ActivityType = EventActivityType.ReadingMissions, AllowedAudience = EventAudience.Parent, StartAt = _now.AddDays(-7), EndAt = _now.AddHours(-3) });
        return (ev, subAct, readAct);
    }

    [Fact]
    public async Task HistoricalApprovedAwards_AppearForClosedEvent_WithoutReopeningAppeals()
    {
        var (ev, subAct, _) = SeedEndedEvent(published: false);
        ev.Status = EventStatus.Closed;
        ev.ResultsFinalizedAt = _now.AddHours(-2);
        var creator = _uow.Seed(new UserAccount { Id = 10, FullName = "Nguyễn Lan", Role = UserRole.ContentCreator });
        var part = _uow.Seed(new EventParticipation { Id = 40, EventId = ev.Id, EventActivityId = subAct.Id, UserId = creator.Id, Status = EventParticipationStatus.Verified });
        _uow.Seed(new EventAward
        {
            Id = 100, EventId = ev.Id, EventActivityId = subAct.Id, ResultSource = EventAwardResultSource.SubmissionScore,
            RankOrTier = "Giải Nhất", SlotIndex = 1, RewardName = "Cúp vàng", ParticipationId = part.Id,
            Status = EventAwardStatus.Fulfilled, ApprovedByAdminId = 2
        });

        var service = new EventResultsQueryService(_uow);
        var result = Assert.Single(await service.GetMyResultsAsync(creator.Id));
        Assert.False(result.CanAppeal);
        Assert.Null(result.AppealDeadlineAt);
        Assert.Equal(EventAwardStatus.Fulfilled, Assert.Single(result.Awards).Status);
        Assert.Equal(ev.ResultsFinalizedAt, (await service.GetPublicResultsAsync(creator.Id, ev.Id)).ResultsPublishedAt);
    }

    [Fact]
    public async Task MyResults_ListsWinnerHeldAward_WithClaimOpensAt()
    {
        var (ev, subAct, _) = SeedEndedEvent(published: true);
        var creator = _uow.Seed(new UserAccount { Id = 10, FullName = "Nguyễn Lan", Role = UserRole.ContentCreator });
        var p = _uow.Seed(new EventParticipation { Id = 40, EventId = 1, EventActivityId = subAct.Id, UserId = creator.Id, Status = EventParticipationStatus.Verified });
        _uow.Seed(new EventAward
        {
            Id = 100, EventId = 1, EventActivityId = subAct.Id, ResultSource = EventAwardResultSource.SubmissionScore,
            RankOrTier = "Giải Nhất", SlotIndex = 1, RewardName = "Cúp vàng", RewardType = EventRewardType.PhysicalGift,
            ParticipationId = p.Id, Status = EventAwardStatus.AppealHold
        });

        var res = await new EventResultsQueryService(_uow).GetMyResultsAsync(creator.Id);
        var item = Assert.Single(res);
        Assert.False(item.CanAppeal);
        var aw = Assert.Single(item.Awards);
        Assert.Equal(EventAwardStatus.AppealHold, aw.Status);
        Assert.Equal(ev.ResultsPublishedAt!.Value.AddHours(48), aw.ClaimOpensAt);
    }

    [Fact]
    public async Task MyResults_NonWinner_HasReasonAndCanAppeal()
    {
        var (_, _, readAct) = SeedEndedEvent(published: true);
        var parent = _uow.Seed(new UserAccount { Id = 11, FullName = "Trần Minh", Role = UserRole.User });
        _uow.Seed(new EventParticipation
        {
            Id = 41, EventId = 1, EventActivityId = readAct.Id, UserId = parent.Id,
            Status = EventParticipationStatus.VerificationFailed,
            VerificationNotesJson = """{"failedTasks":[{"TaskCode":"T02","Title":"Thảo luận"}]}"""
        });
        _uow.Seed(new EventAward
        {
            Id = 101, EventId = 1, EventActivityId = readAct.Id, ResultSource = EventAwardResultSource.MissionCompletion,
            RankOrTier = "Hoàn thành", SlotIndex = 1, RewardName = "Lượt AI", Status = EventAwardStatus.AppealHold, ParticipationId = 999
        });

        var res = await new EventResultsQueryService(_uow).GetMyResultsAsync(parent.Id);
        var item = Assert.Single(res);
        Assert.True(item.CanAppeal);
        Assert.Empty(item.Awards);
        var actRes = Assert.Single(item.Activities);
        Assert.Contains("T02", actRes.Reason);
        Assert.Equal(41, actRes.ParticipationId);
        Assert.True(actRes.CanAppeal);
    }

    [Fact]
    public async Task MyResults_OnlySourceActivityParticipationIsAppealable()
    {
        var (_, _, readAct) = SeedEndedEvent(published: true);
        var voteAct = _uow.Seed(new EventActivity { Id = 22, EventId = 1, ActivityType = EventActivityType.Voting, AllowedAudience = EventAudience.Parent, StartAt = _now.AddDays(-7), EndAt = _now.AddHours(-3) });
        var parent = _uow.Seed(new UserAccount { Id = 11, FullName = "Trần Minh", Role = UserRole.User });
        // Hồ sơ bình chọn (không phải nguồn xét giải) đứng trước hồ sơ đọc truyện.
        _uow.Seed(new EventParticipation { Id = 40, EventId = 1, EventActivityId = voteAct.Id, UserId = parent.Id, Status = EventParticipationStatus.Verified });
        _uow.Seed(new EventParticipation { Id = 41, EventId = 1, EventActivityId = readAct.Id, UserId = parent.Id, Status = EventParticipationStatus.Verified });
        _uow.Seed(new EventAward
        {
            Id = 101, EventId = 1, EventActivityId = readAct.Id, ResultSource = EventAwardResultSource.MissionCompletion,
            RankOrTier = "Hoàn thành", SlotIndex = 1, RewardName = "Lượt AI", Status = EventAwardStatus.AppealHold, ParticipationId = 999
        });

        var item = Assert.Single(await new EventResultsQueryService(_uow).GetMyResultsAsync(parent.Id));

        Assert.True(item.CanAppeal);
        Assert.False(item.Activities.Single(a => a.ParticipationId == 40).CanAppeal);
        Assert.True(item.Activities.Single(a => a.ParticipationId == 41).CanAppeal);
    }

    [Fact]
    public async Task MyResults_IncludesRevokedAwardWithReason()
    {
        var (_, subAct, _) = SeedEndedEvent(published: true);
        var creator = _uow.Seed(new UserAccount { Id = 10, FullName = "Nguyễn Lan", Role = UserRole.ContentCreator });
        var p = _uow.Seed(new EventParticipation { Id = 40, EventId = 1, EventActivityId = subAct.Id, UserId = creator.Id, Status = EventParticipationStatus.Verified });
        _uow.Seed(new EventAward
        {
            Id = 100, EventId = 1, EventActivityId = subAct.Id, ResultSource = EventAwardResultSource.SubmissionScore,
            RankOrTier = "Giải Nhất", SlotIndex = 1, RewardName = "Cúp vàng", ParticipationId = p.Id,
            Status = EventAwardStatus.Revoked, RevokedReason = "Vi phạm quy chế"
        });

        var res = await new EventResultsQueryService(_uow).GetMyResultsAsync(creator.Id);
        var aw = Assert.Single(Assert.Single(res).Awards);
        Assert.Equal(EventAwardStatus.Revoked, aw.Status);
        Assert.Equal("Vi phạm quy chế", aw.RevokedReason);
    }

    [Fact]
    public async Task MyResults_BeforePublication_HasRecapButNoAwards()
    {
        var (_, _, readAct) = SeedEndedEvent(published: false);
        var parent = _uow.Seed(new UserAccount { Id = 11, FullName = "Trần Minh", Role = UserRole.User });
        var child = _uow.Seed(new ChildProfile { Id = 200, OwnerUserId = parent.Id, Nickname = "Sóc" });
        var p = _uow.Seed(new EventParticipation { Id = 41, EventId = 1, EventActivityId = readAct.Id, UserId = parent.Id, Status = EventParticipationStatus.Verified });
        _uow.Seed(new EventStory { Id = 50, EventId = 1, StoryId = 301, StoryVersionId = 401, IsActive = true });
        _uow.Seed(new ReadingSession { Id = 60, ChildProfileId = child.Id, StoryId = 301, StoryVersionId = 401, Status = SessionStatus.Completed, CompletedAt = _now.AddDays(-2) });
        _uow.Seed(new EventAward { Id = 101, EventId = 1, EventActivityId = readAct.Id, ResultSource = EventAwardResultSource.MissionCompletion, RankOrTier = "Hoàn thành", SlotIndex = 1, RewardName = "Quà", ParticipationId = p.Id, Status = EventAwardStatus.AwardProposed });

        var res = await new EventResultsQueryService(_uow).GetMyResultsAsync(parent.Id);
        var item = Assert.Single(res);
        Assert.Null(item.ResultsPublishedAt);
        Assert.Equal(1, item.Recap.StoriesRead);
        Assert.Empty(item.Awards);
    }

    [Fact]
    public async Task PublicResults_StoryAward_MissionConsent_RevokedExcluded_AndBeforePublish409()
    {
        var (ev, subAct, readAct) = SeedEndedEvent(published: false);
        var svc = new EventResultsQueryService(_uow);
        await Assert.ThrowsAsync<ConflictException>(() => svc.GetPublicResultsAsync(10, 1));

        ev.ResultsPublishedAt = _now.AddHours(-1);
        var creator = _uow.Seed(new UserAccount { Id = 10, FullName = "Tác giả Hoa", Role = UserRole.ContentCreator });
        var parentNoConsent = _uow.Seed(new UserAccount { Id = 11, FullName = "Mẹ Lan", Role = UserRole.User });
        var parentConsent = _uow.Seed(new UserAccount { Id = 12, FullName = "Bố Nam", Role = UserRole.User });

        var c1 = _uow.Seed(new ChildProfile { Id = 201, OwnerUserId = parentNoConsent.Id, Nickname = "Bông" });
        var c2 = _uow.Seed(new ChildProfile { Id = 202, OwnerUserId = parentNoConsent.Id, Nickname = "Bắp" });
        var c3 = _uow.Seed(new ChildProfile { Id = 203, OwnerUserId = parentConsent.Id, Nickname = "Cún" });

        var pCreator = _uow.Seed(new EventParticipation { Id = 40, EventId = 1, EventActivityId = subAct.Id, UserId = creator.Id, Status = EventParticipationStatus.Verified });
        var pNo = _uow.Seed(new EventParticipation { Id = 41, EventId = 1, EventActivityId = readAct.Id, UserId = parentNoConsent.Id, Status = EventParticipationStatus.Verified });
        var pYes = _uow.Seed(new EventParticipation { Id = 42, EventId = 1, EventActivityId = readAct.Id, UserId = parentConsent.Id, Status = EventParticipationStatus.Verified });

        _uow.Seed(new Story { Id = 301, Title = "Hành trình gió", AuthorUserId = creator.Id, IsPublished = true });
        _uow.Seed(new EventSubmission { Id = 501, EventId = 1, ParticipationId = pCreator.Id, EventActivityId = subAct.Id, CreatorUserId = creator.Id, StoryId = 301, CurrentStoryVersionId = 401, LockedStoryVersionId = 401, Status = EventSubmissionStatus.Approved });
        _uow.Seed(new EventStory { Id = 50, EventId = 1, StoryId = 301, StoryVersionId = 401, IsActive = true });

        _uow.Seed(new ReadingSession { Id = 60, ChildProfileId = c1.Id, StoryId = 301, StoryVersionId = 401, Status = SessionStatus.Completed, CompletedAt = _now.AddDays(-2) });
        _uow.Seed(new ReadingSession { Id = 61, ChildProfileId = c2.Id, StoryId = 301, StoryVersionId = 401, Status = SessionStatus.Completed, CompletedAt = _now.AddDays(-2) });
        _uow.Seed(new ReadingSession { Id = 62, ChildProfileId = c3.Id, StoryId = 301, StoryVersionId = 401, Status = SessionStatus.Completed, CompletedAt = _now.AddDays(-2) });

        _uow.Seed(new EventAward { Id = 100, EventId = 1, EventActivityId = subAct.Id, ResultSource = EventAwardResultSource.SubmissionScore, RankOrTier = "Giải Nhất", SlotIndex = 1, RewardName = "Cúp", ParticipationId = pCreator.Id, Status = EventAwardStatus.AppealHold });
        _uow.Seed(new EventAward { Id = 101, EventId = 1, EventActivityId = readAct.Id, ResultSource = EventAwardResultSource.MissionCompletion, RankOrTier = "Đọc Tích Cực", SlotIndex = 1, RewardName = "Lượt AI", ParticipationId = pNo.Id, Status = EventAwardStatus.Fulfilled, ShowChildNames = false });
        _uow.Seed(new EventAward { Id = 102, EventId = 1, EventActivityId = readAct.Id, ResultSource = EventAwardResultSource.MissionCompletion, RankOrTier = "Đọc Tích Cực", SlotIndex = 2, RewardName = "Lượt AI", ParticipationId = pYes.Id, Status = EventAwardStatus.Fulfilled, ShowChildNames = true });
        _uow.Seed(new EventAward { Id = 103, EventId = 1, EventActivityId = subAct.Id, ResultSource = EventAwardResultSource.SubmissionScore, RankOrTier = "Giải Bị Thu Hồi", SlotIndex = 1, RewardName = "Quà", ParticipationId = pCreator.Id, Status = EventAwardStatus.Revoked, RevokedReason = "Thu hồi" });

        var pub = await svc.GetPublicResultsAsync(parentNoConsent.Id, 1);
        Assert.Equal(3, pub.ParticipantCount);
        Assert.Equal(1, pub.StoryCount);
        Assert.Equal(3, pub.Awards.Count);
        Assert.DoesNotContain(pub.Awards, a => a.RankOrTier == "Giải Bị Thu Hồi");

        var storyAw = pub.Awards.Single(a => a.RankOrTier == "Giải Nhất");
        Assert.Equal("Hành trình gió", storyAw.StoryTitle);
        Assert.Equal("Tác giả Hoa", storyAw.WinnerName);

        var noConsentAw = pub.Awards.Single(a => a.WinnerName == "Mẹ Lan");
        Assert.Equal(2, noConsentAw.ChildCount);
        Assert.Empty(noConsentAw.ChildNicknames);

        var consentAw = pub.Awards.Single(a => a.WinnerName == "Bố Nam");
        Assert.Equal(1, consentAw.ChildCount);
        Assert.Equal(new[] { "Cún" }, consentAw.ChildNicknames);
    }
}
