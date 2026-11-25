using Moq;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Common.Time;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.Events.DTOs;
using StoryPlatform.Application.Features.Events.Services;
using StoryPlatform.Application.Features.Moderation.DTOs;
using StoryPlatform.Application.Features.Moderation.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.UnitTests.Features.TokenQuota;
using Xunit;

namespace StoryPlatform.UnitTests.Features.Events;

public sealed class ActivityBasedEventTests
{
    private readonly InMemoryUnitOfWork _uow = new();
    private readonly Mock<IAuditLogWriter> _audit = new();
    private readonly DateTime _now = DateTime.UtcNow;

    [Fact]
    public async Task Dashboard_KeepsCuratedReadingSeparateFromEligibleVotingStories()
    {
        _uow.Seed(new Event { Id = 1, Status = EventStatus.Published, ConfigVersion = EventConfigVersion.ActivityBased });
        _uow.Seed(new EventActivity { Id = 2, EventId = 1, ActivityType = EventActivityType.ReadingMissions,
            StorySource = EventReadingStorySource.CuratedOnly });
        _uow.Seed(new EventActivity { Id = 3, EventId = 1, ActivityType = EventActivityType.Voting,
            StartAt = _now.AddHours(-1), EndAt = _now.AddHours(1) });
        _uow.Seed(new EventRuleVersion { Id = 4, EventId = 1 });
        _uow.Seed(new EventParticipation { Id = 5, EventId = 1, EventActivityId = 2, UserId = 10, AcceptedRuleVersionId = 4 });
        _uow.Seed(new EventParticipation { Id = 6, EventId = 1, EventActivityId = 3, UserId = 10, AcceptedRuleVersionId = 4 });
        _uow.Seed(new ChildProfile { Id = 7, OwnerUserId = 10 });
        _uow.Seed(new Story { Id = 11, IsPublished = true, AuthorUserId = 20 });
        _uow.Seed(new Story { Id = 12, IsPublished = true, AuthorUserId = 20 });
        _uow.Seed(new Story { Id = 13, IsPublished = true, AuthorUserId = 20 });
        _uow.Seed(new EventSubmission { Id = 14, EventId = 1, StoryId = 12, LockedStoryVersionId = 21,
            Status = EventSubmissionStatus.Approved });
        _uow.Seed(new EventSubmission { Id = 15, EventId = 1, StoryId = 13, LockedStoryVersionId = 22,
            Status = EventSubmissionStatus.Approved });
        _uow.Seed(new EventStory { EventId = 1, StoryId = 11, StoryVersionId = 20, Source = EventStorySource.ModeratorSeed });
        _uow.Seed(new EventStory { EventId = 1, StoryId = 12, StoryVersionId = 21, Source = EventStorySource.CreatorSubmission,
            EventSubmissionId = 14 });
        _uow.Seed(new EventStory { EventId = 1, StoryId = 13, StoryVersionId = 23, Source = EventStorySource.CreatorSubmission,
            EventSubmissionId = 15 });
        var read = _uow.Seed(new ReadingSession { ChildProfileId = 7, StoryId = 12, StoryVersionId = 21,
            Status = SessionStatus.Completed, CompletedAt = _now.AddHours(-2) });
        var service = new EventParticipationService(_uow, _audit.Object);

        var before = await service.GetMissionDashboardAsync(10, 1);
        Assert.Equal(11, Assert.Single(before.Stories).StoryId);
        var candidate = Assert.Single(before.VotingStories);
        Assert.Equal(12, candidate.StoryId);
        Assert.False(candidate.HasFamilyRead);

        read.CompletedAt = _now;
        var after = await service.GetMissionDashboardAsync(10, 1);
        Assert.True(Assert.Single(after.VotingStories).HasFamilyRead);
        Assert.Empty((await service.GetMissionDashboardAsync(10, 1)).Tasks);
    }

    [Fact]
    public async Task TaskFilterStories_UsesOnlyPublicEventStoriesAndMarksReadingSource()
    {
        _uow.Seed(new Event { Id = 1, ConfigVersion = EventConfigVersion.ActivityBased });
        _uow.Seed(new EventActivity { Id = 2, EventId = 1, ActivityType = EventActivityType.ReadingMissions,
            StorySource = EventReadingStorySource.CuratedOnly });
        _uow.Seed(new Story { Id = 11, Title = "A curated story", IsPublished = true, OutputMode = StoryOutputMode.Learning });
        _uow.Seed(new Story { Id = 12, Title = "A published submission", IsPublished = true });
        _uow.Seed(new Story { Id = 13, Title = "Private Parent", IsPublished = true });
        _uow.Seed(new Story { Id = 14, Title = "Unpublished" });
        _uow.Seed(new Story { Id = 15, Title = "Another event", IsPublished = true });
        _uow.Seed(new EventStory { EventId = 1, StoryId = 11, IsActive = true, Source = EventStorySource.ModeratorSeed });
        _uow.Seed(new EventStory { EventId = 1, StoryId = 12, IsActive = true, Source = EventStorySource.CreatorSubmission });
        _uow.Seed(new EventStory { EventId = 1, StoryId = 13, IsActive = true, Source = EventStorySource.ParentSubmission });
        _uow.Seed(new EventStory { EventId = 1, StoryId = 14, IsActive = true, Source = EventStorySource.ModeratorSeed });
        _uow.Seed(new EventStory { EventId = 3, StoryId = 15, IsActive = true, Source = EventStorySource.ModeratorSeed });

        var options = await new EventManagementService(_uow, _audit.Object).ListTaskFilterStoriesAsync(1);

        Assert.Collection(options,
            story => Assert.Equal(new EventTaskStoryOptionDto(11, "A curated story", EventStorySource.ModeratorSeed, true, true), story),
            story => Assert.Equal(12, story.Id));
        Assert.False(options[1].CanUseForReading);
    }

    [Fact]
    public async Task EventWindow_PersistsAndRejectsActivityDatesOutsideIt()
    {
        _uow.Seed(new ContentCategory { Id = 1, IsActive = true });
        var service = new EventManagementService(_uow, _audit.Object);
        var request = new CreateEventRequestDto
        {
            Title = "Event window", MainCategoryId = 1, ConfigVersion = EventConfigVersion.ActivityBased,
            OpensAt = _now, ClosesAt = _now.AddDays(7),
            Activities = [new UpsertEventActivityDto
            {
                ActivityType = EventActivityType.Submission, RegistrationStartAt = _now,
                RegistrationEndAt = _now.AddDays(2), StartAt = _now, EndAt = _now.AddDays(4),
                ReviewCutoffAt = _now.AddDays(8)
            }]
        };

        var invalid = await Assert.ThrowsAsync<BadRequestException>(() => service.CreateEventAsync(9, request));
        Assert.Contains("Hạn duyệt bài", invalid.Message);
        request.Activities[0].ReviewCutoffAt = request.ClosesAt;
        var created = await service.CreateEventAsync(9, request);
        Assert.Equal(request.OpensAt, created.OpensAt);
        Assert.Equal(request.ClosesAt, created.ClosesAt);

        request.OpensAt = null;
        request.ClosesAt = null;
        request.Activities[0].RegistrationStartAt = _now.AddDays(-1);
        var invalidUpdate = await Assert.ThrowsAsync<BadRequestException>(() => service.UpdateEventAsync(9, created.Id, request));
        Assert.Contains("Mở đăng ký", invalidUpdate.Message);
    }

    [Fact]
    public async Task Verification_WaitsForTheEventWindowEvenWhenActivitiesHaveEnded()
    {
        _uow.Seed(new Event { Id = 1, Status = EventStatus.Published, ConfigVersion = EventConfigVersion.ActivityBased,
            OpensAt = _now.AddDays(-2), ClosesAt = _now.AddHours(1), AssignedModeratorId = 9 });
        _uow.Seed(new EventActivity { Id = 20, EventId = 1, ActivityType = EventActivityType.Submission,
            EndAt = _now.AddHours(-1) });
        var service = new EventParticipationService(_uow, _audit.Object);

        await Assert.ThrowsAsync<ConflictException>(() => service.VerifyEventParticipationsAsync(9, 1));
    }

    [Fact]
    public async Task RewardPlanCatalog_ListsOnlySelectableActiveCreatorPacks()
    {
        _uow.Seed(new SubscriptionPlan { Id = 1, Name = "Creator Standard", Audience = PlanAudience.Creator,
            PlanType = PlanType.CreatorPack, AiCredits = 5, SubmitCredits = 2 });
        _uow.Seed(new SubscriptionPlan { Id = 2, Name = "Inactive", Audience = PlanAudience.Creator,
            PlanType = PlanType.CreatorPack, IsActive = false });
        _uow.Seed(new SubscriptionPlan { Id = 3, Name = "Reading", Audience = PlanAudience.User });
        _uow.Seed(new SubscriptionPlan { Id = 4, Name = "Ambiguous", Audience = PlanAudience.Creator,
            PlanType = PlanType.CreatorPack });
        _uow.Seed(new SubscriptionPlan { Id = 5, Name = "ambiguous", Audience = PlanAudience.Creator,
            PlanType = PlanType.CreatorPack });

        var service = new EventManagementService(_uow, _audit.Object);
        var plan = Assert.Single(await service.ListCreatorRewardPlansAsync());

        Assert.Equal(new EventRewardPlanDto(1, "Creator Standard", 5, 2), plan);
    }

    [Fact]
    public async Task DeleteDraft_SoftDeletesDraftAndRejectsNonDraftOrRepeatedDelete()
    {
        var draft = _uow.Seed(new Event { Id = 1, Status = EventStatus.Draft });
        var published = _uow.Seed(new Event { Id = 2, Status = EventStatus.Published });
        var service = new EventManagementService(_uow, _audit.Object);

        await Assert.ThrowsAsync<ConflictException>(() => service.DeleteDraftAsync(9, published.Id));
        Assert.False(published.IsDeleted);
        Assert.True(await service.DeleteDraftAsync(9, draft.Id));
        Assert.True(draft.IsDeleted);
        await Assert.ThrowsAsync<NotFoundException>(() => service.DeleteDraftAsync(9, draft.Id));
    }

    [Fact]
    public void Evaluate_EnforceMandatoryInAllModes_BlocksWhenMandatoryIncomplete_ButLegacyAllows()
    {
        var tasks = new List<EventTaskDefinition>
        {
            new() { Id = 1, TaskCode = "M1", VerifierType = TaskVerifierType.GuidedDiscussionCheckin, TargetValue = 1, IsMandatory = true, Points = 20 },
            new() { Id = 2, TaskCode = "O1", VerifierType = TaskVerifierType.RealLifePracticeCheckin, TargetValue = 1, IsMandatory = false, Points = 60 },
        };
        var onlyOptionalProgress = new List<EventTaskProgress>
        {
            new() { Id = 10, TaskDefinitionId = 2, StoryId = 100, ActivityLocalDate = DateOnly.FromDateTime(_now) }
        };

        var pointsRule = new EventRuleVersion { CompletionMode = TrackCompletionMode.MinimumPointsThreshold, RequiredPoints = 50 };
        var countRule = new EventRuleVersion { CompletionMode = TrackCompletionMode.MinimumTasksCount, RequiredTaskCount = 1 };

        Assert.True(EventMissionEvaluator.Evaluate(pointsRule, tasks, onlyOptionalProgress, enforceMandatoryInAllModes: false).AllRequirementsMet);
        Assert.False(EventMissionEvaluator.Evaluate(countRule, tasks, onlyOptionalProgress, enforceMandatoryInAllModes: false).AllRequirementsMet);

        Assert.False(EventMissionEvaluator.Evaluate(pointsRule, tasks, onlyOptionalProgress, enforceMandatoryInAllModes: true).AllRequirementsMet);
        Assert.False(EventMissionEvaluator.Evaluate(countRule, tasks, onlyOptionalProgress, enforceMandatoryInAllModes: true).AllRequirementsMet);

        var bothProgress = new List<EventTaskProgress>(onlyOptionalProgress)
        {
            new() { Id = 11, TaskDefinitionId = 1, StoryId = 100, ActivityLocalDate = DateOnly.FromDateTime(_now) }
        };
        Assert.True(EventMissionEvaluator.Evaluate(pointsRule, tasks, bothProgress, enforceMandatoryInAllModes: true).AllRequirementsMet);
        Assert.True(EventMissionEvaluator.Evaluate(countRule, tasks, bothProgress, enforceMandatoryInAllModes: true).AllRequirementsMet);
    }

    [Fact]
    public async Task SubmitActivityStory_EnforcesMaxSubmissions_ResubmitChangesRequested_UnlimitedNull_AndWrongRole()
    {
        var creator = _uow.Seed(new UserAccount { Id = 10, Email = "c@test.local", Role = UserRole.ContentCreator });
        var parent = _uow.Seed(new UserAccount { Id = 11, Email = "p@test.local", Role = UserRole.User });
        _uow.Seed(new Event
        {
            Id = 1, Title = "Sáng tác V2", Slug = "v2-sub", MainCategoryId = 1, Status = EventStatus.Published,
            ConfigVersion = EventConfigVersion.ActivityBased, CreatedByUserId = 1, AssignedModeratorId = 1
        });
        var subAct = _uow.Seed(new EventActivity
        {
            Id = 20, EventId = 1, ActivityType = EventActivityType.Submission, AllowedAudience = EventAudience.Creator,
            RegistrationStartAt = _now.AddDays(-2), RegistrationEndAt = _now.AddDays(2),
            StartAt = _now.AddDays(-2), EndAt = _now.AddDays(2),
            MaxSubmissionsPerUser = 2, GrantAiCredits = 2
        });
        _uow.Seed(new EventRuleVersion { Id = 30, EventId = 1, IsActive = true, RulesText = "Thể lệ" });
        _uow.Seed(new EventParticipation { Id = 40, EventId = 1, EventActivityId = subAct.Id, UserId = creator.Id, AcceptedRuleVersionId = 30 });

        for (var i = 1; i <= 3; i++)
        {
            _uow.Seed(new Story { Id = 100 + i, AuthorUserId = creator.Id, Scope = StoryScope.Creator, Status = StoryStatus.Ready, Title = $"S{i}" });
            _uow.Seed(new StoryVersion { Id = 200 + i, StoryId = 100 + i, VersionNo = 1, IsCurrent = true, Title = $"S{i}", Content = $"Nội dung độc lập số {i} để kiểm tra." });
        }

        var pubMock = new Mock<IPublicStorySubmissionService>();
        var svc = new EventParticipationService(_uow, _audit.Object, publicSubmission: pubMock.Object);

        // Wrong role cannot register or submit when AllowedAudience == Creator.
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            svc.RegisterAsync(parent.Id, 1, new RegisterEventRequestDto { ActivityType = EventActivityType.Submission, AcceptRules = true }));

        var s1 = await svc.SubmitActivityStoryAsync(creator.Id, 1, new CreatorEventSubmitRequestDto { StoryId = 101 });
        var s2 = await svc.SubmitActivityStoryAsync(creator.Id, 1, new CreatorEventSubmitRequestDto { StoryId = 102 });
        Assert.NotEqual(s1.Id, s2.Id);
        pubMock.Verify(x => x.SubmitForEventAsync(creator.Id, It.IsAny<SubmitPublicStoryRequestDto>(), true, 1, It.IsAny<CancellationToken>()), Times.Exactly(2));

        // 3rd distinct story exceeds MaxSubmissionsPerUser = 2.
        await Assert.ThrowsAsync<ConflictException>(() =>
            svc.SubmitActivityStoryAsync(creator.Id, 1, new CreatorEventSubmitRequestDto { StoryId = 103 }));

        // ChangesRequested on story 101 allows resubmitting same story without incrementing distinct story count.
        var sub1Entity = _uow.Items<EventSubmission>().Single(s => s.Id == s1.Id);
        sub1Entity.Status = EventSubmissionStatus.ChangesRequested;
        sub1Entity.LockedStoryVersionId = null;
        var resub = await svc.SubmitActivityStoryAsync(creator.Id, 1, new CreatorEventSubmitRequestDto { StoryId = 101 });
        Assert.Equal(s1.Id, resub.Id);
        Assert.Equal(2, _uow.Items<EventSubmission>().Count(s => s.EventId == 1 && s.CreatorUserId == creator.Id));

        // Null MaxSubmissionsPerUser allows unlimited distinct stories.
        subAct.MaxSubmissionsPerUser = null;
        var s3 = await svc.SubmitActivityStoryAsync(creator.Id, 1, new CreatorEventSubmitRequestDto { StoryId = 103 });
        Assert.Equal(103, s3.StoryId);
    }

    [Fact]
    public async Task CastVote_ActivityBased_BlocksSelfVote_OutsideWindow_AndMismatchedVersionSession()
    {
        var authorParent = _uow.Seed(new UserAccount { Id = 10, Email = "p1@test.local", Role = UserRole.User });
        var voterParent = _uow.Seed(new UserAccount { Id = 11, Email = "p2@test.local", Role = UserRole.User });
        var authorChild = _uow.Seed(new ChildProfile { Id = 50, OwnerUserId = authorParent.Id, Status = ChildProfileStatus.Active });
        var voterChild = _uow.Seed(new ChildProfile { Id = 51, OwnerUserId = voterParent.Id, Status = ChildProfileStatus.Active });

        _uow.Seed(new Event { Id = 1, Status = EventStatus.Published, ConfigVersion = EventConfigVersion.ActivityBased });
        var voteAct = _uow.Seed(new EventActivity
        {
            Id = 20, EventId = 1, ActivityType = EventActivityType.Voting, AllowedAudience = EventAudience.Parent,
            RegistrationStartAt = _now.AddDays(-2), RegistrationEndAt = _now.AddDays(2),
            StartAt = _now.AddHours(-2), EndAt = _now.AddHours(2), MinVotesPerSubmission = 1
        });
        _uow.Seed(new EventRuleVersion { Id = 30, EventId = 1, IsActive = true, RulesText = "Vote rules" });
        _uow.Seed(new EventParticipation { Id = 40, EventId = 1, EventActivityId = voteAct.Id, UserId = authorParent.Id, AcceptedRuleVersionId = 30 });
        _uow.Seed(new EventParticipation { Id = 41, EventId = 1, EventActivityId = voteAct.Id, UserId = voterParent.Id, AcceptedRuleVersionId = 30 });

        _uow.Seed(new Story { Id = 100, AuthorUserId = authorParent.Id, IsPublished = true, Status = StoryStatus.Ready });
        _uow.Seed(new StoryVersion { Id = 200, StoryId = 100, VersionNo = 2, IsCurrent = true });
        _uow.Seed(new EventSubmission { Id = 250, EventId = 1, StoryId = 100, LockedStoryVersionId = 200, Status = EventSubmissionStatus.Approved });
        _uow.Seed(new EventStory { Id = 300, EventId = 1, StoryId = 100, StoryVersionId = 200, Source = EventStorySource.CreatorSubmission, EventSubmissionId = 250, IsActive = true });
        _uow.Seed(new EventStory { Id = 301, EventId = 1, StoryId = 101, StoryVersionId = 201, Source = EventStorySource.ModeratorSeed, IsActive = true });

        _uow.Seed(new ReadingSession { Id = 400, ChildProfileId = authorChild.Id, StoryId = 100, StoryVersionId = 200, Status = SessionStatus.Completed, StartedAt = _now.AddMinutes(-20), CompletedAt = _now.AddMinutes(-5) });
        _uow.Seed(new ReadingSession { Id = 401, ChildProfileId = voterChild.Id, StoryId = 100, StoryVersionId = 199, Status = SessionStatus.Completed, StartedAt = _now.AddMinutes(-20), CompletedAt = _now.AddMinutes(-5) });

        var svc = new EventParticipationService(_uow, _audit.Object);

        await Assert.ThrowsAsync<ConflictException>(() =>
            svc.CastVoteAsync(voterParent.Id, 1, new CastEventVoteRequestDto { StoryId = 101, Stars = 5 }));

        // Author cannot self-vote even with completed reading session on exact version.
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            svc.CastVoteAsync(authorParent.Id, 1, new CastEventVoteRequestDto { StoryId = 100, Stars = 5 }));

        // Mismatched StoryVersionId (199 vs 200) is rejected.
        await Assert.ThrowsAsync<ConflictException>(() =>
            svc.CastVoteAsync(voterParent.Id, 1, new CastEventVoteRequestDto { StoryId = 100, Stars = 5 }));

        // Matching version outside voting window is rejected.
        _uow.Seed(new ReadingSession { Id = 402, ChildProfileId = voterChild.Id, StoryId = 100, StoryVersionId = 200, Status = SessionStatus.Completed, StartedAt = _now.AddMinutes(-10), CompletedAt = _now.AddMinutes(-2) });
        voteAct.EndAt = _now.AddMinutes(-1);
        await Assert.ThrowsAsync<ConflictException>(() =>
            svc.CastVoteAsync(voterParent.Id, 1, new CastEventVoteRequestDto { StoryId = 100, Stars = 4 }));
    }

    [Fact]
    public async Task Verification_WaitsUntilSubmissionReviewCutoff()
    {
        _uow.Seed(new Event { Id = 1, Status = EventStatus.Published, ConfigVersion = EventConfigVersion.ActivityBased, AssignedModeratorId = 9 });
        _uow.Seed(new EventActivity
        {
            Id = 20, EventId = 1, ActivityType = EventActivityType.Submission,
            EndAt = _now.AddHours(-1), ReviewCutoffAt = _now.AddHours(1)
        });

        var service = new EventParticipationService(_uow, _audit.Object);
        await Assert.ThrowsAsync<ConflictException>(() => service.VerifyEventParticipationsAsync(9, 1));
    }

    [Fact]
    public async Task CloneLegacyDraftToActivityBased_MapsCompatibleFields_AndReportsUnmappedWarnings()
    {
        _uow.Seed(new ContentCategory { Id = 1, Code = "story", DisplayName = "Truyện", IsActive = true });
        _uow.Seed(new Event
        {
            Id = 1, Title = "Sự kiện Cũ", Slug = "old-draft", Description = "Mô tả", MainCategoryId = 1,
            Status = EventStatus.Draft, ConfigVersion = EventConfigVersion.LegacyRoleBased,
            Format = EventFormat.Standard, ScoringMode = EventScoringMode.ParentVoteAverage,
            BudgetVnd = 1_000_000L, CreatedByUserId = 9, AssignedModeratorId = 9
        });
        var cTrack = _uow.Seed(new EventTrack
        {
            Id = 10, EventId = 1, TrackType = EventTrackType.Creator,
            RegistrationStartAt = _now, RegistrationEndAt = _now.AddDays(5),
            ExecutionStartAt = _now, ExecutionEndAt = _now.AddDays(5),
            GrantAiCredits = 2, GrantSubmitCredits = 3, MaxSubmissionsPerUser = 1
        });
        var pTrack = _uow.Seed(new EventTrack
        {
            Id = 11, EventId = 1, TrackType = EventTrackType.Parent,
            RegistrationStartAt = _now, RegistrationEndAt = _now.AddDays(10),
            ExecutionStartAt = _now.AddDays(6), ExecutionEndAt = _now.AddDays(10),
            AwardAllMissionCompleters = true
        });
        _uow.Seed(new EventRuleVersion { Id = 20, EventId = 1, IsActive = true, VersionNo = 1, RulesText = "Thể lệ cũ" });
        _uow.Seed(new EventTaskDefinition { Id = 30, EventRuleVersionId = 20, TargetTrackType = EventTrackType.Creator, VerifierType = TaskVerifierType.SubmitApprovedStory, Title = "Nộp bài", TargetValue = 1 });
        _uow.Seed(new EventTaskDefinition { Id = 31, EventRuleVersionId = 20, TargetTrackType = EventTrackType.Parent, VerifierType = TaskVerifierType.ReadEventStories, Title = "Đọc truyện", TargetValue = 2 });
        _uow.Seed(new EventAward { Id = 40, EventId = 1, TrackId = cTrack.Id, RankOrTier = "Giải Nhất", RewardType = EventRewardType.PhysicalGift, RewardName = "Quà", UnitValueVnd = 100_000L });
        _uow.Seed(new EventAward { Id = 41, EventId = 1, TrackId = pTrack.Id, RankOrTier = "Quà Đọc", RewardType = EventRewardType.PlatformCredits, RewardName = "Credit", CreditGrantAmount = 1 });

        var svc = new EventManagementService(_uow, _audit.Object);
        var cloned = await svc.CloneLegacyDraftToActivityBasedAsync(9, 1);

        Assert.Equal(EventConfigVersion.ActivityBased, cloned.Draft.ConfigVersion);
        Assert.Equal(EventStatus.Draft, cloned.Draft.Status);
        Assert.Contains(cloned.Draft.Activities, a => a.ActivityType == EventActivityType.Submission && a.GrantAiCredits == 2);
        Assert.Contains(cloned.Draft.Activities, a => a.ActivityType == EventActivityType.ReadingMissions);
        Assert.DoesNotContain(cloned.Draft.Activities, a => a.ActivityType == EventActivityType.Voting);
        Assert.NotNull(cloned.Draft.ActiveRule);
        Assert.Single(cloned.Draft.ActiveRule!.Tasks);
        Assert.Contains(cloned.UnmappedWarnings, w => w.Contains("Tặng lượt nộp miễn phí"));
        Assert.Contains(cloned.UnmappedWarnings, w => w.Contains("Bình chọn"));
        Assert.Contains(cloned.UnmappedWarnings, w => w.Contains("Giải Nhất"));
    }

    [Fact]
    public async Task ProposeAwards_And_ProcessExpiredClaims_BestEntry_SingleSlotPerTier_AllowsCrossTier()
    {
        var creator1 = _uow.Seed(new UserAccount { Id = 10, Email = "c1@test.local", Role = UserRole.ContentCreator });
        var creator2 = _uow.Seed(new UserAccount { Id = 11, Email = "c2@test.local", Role = UserRole.ContentCreator });
        var parent1 = _uow.Seed(new UserAccount { Id = 12, Email = "p1@test.local", Role = UserRole.User });
        var parent2 = _uow.Seed(new UserAccount { Id = 13, Email = "p2@test.local", Role = UserRole.User });

        _uow.Seed(new Event { Id = 1, Title = "V2 Awards", Slug = "v2-awards", MainCategoryId = 1, Status = EventStatus.Published, ConfigVersion = EventConfigVersion.ActivityBased, AssignedModeratorId = 1 });
        var subAct = _uow.Seed(new EventActivity { Id = 20, EventId = 1, ActivityType = EventActivityType.Submission, AllowedAudience = EventAudience.Both, StartAt = _now.AddDays(-5), EndAt = _now.AddHours(-3), ClaimWindowHours = 24, MaxReallocationRounds = 1 });
        var readAct = _uow.Seed(new EventActivity { Id = 21, EventId = 1, ActivityType = EventActivityType.ReadingMissions, AllowedAudience = EventAudience.Parent, StartAt = _now.AddDays(-5), EndAt = _now.AddHours(-2), ClaimWindowHours = 24, MaxReallocationRounds = 1 });
        var voteAct = _uow.Seed(new EventActivity { Id = 22, EventId = 1, ActivityType = EventActivityType.Voting, AllowedAudience = EventAudience.Parent, StartAt = _now.AddHours(-3), EndAt = _now.AddHours(-1), MinVotesPerSubmission = 1, ClaimWindowHours = 24, MaxReallocationRounds = 1 });
        _uow.Seed(new EventRuleVersion { Id = 30, EventId = 1, IsActive = true, RulesText = "Rules" });

        var pC1 = _uow.Seed(new EventParticipation { Id = 40, EventId = 1, EventActivityId = subAct.Id, UserId = creator1.Id, AcceptedRuleVersionId = 30, Status = EventParticipationStatus.Verified });
        var pC2 = _uow.Seed(new EventParticipation { Id = 41, EventId = 1, EventActivityId = subAct.Id, UserId = creator2.Id, AcceptedRuleVersionId = 30, Status = EventParticipationStatus.Verified });
        var pRead1 = _uow.Seed(new EventParticipation { Id = 42, EventId = 1, EventActivityId = readAct.Id, UserId = parent1.Id, AcceptedRuleVersionId = 30, Status = EventParticipationStatus.Verified, MissionCompletedAt = _now.AddHours(-3) });
        var pRead2 = _uow.Seed(new EventParticipation { Id = 43, EventId = 1, EventActivityId = readAct.Id, UserId = parent2.Id, AcceptedRuleVersionId = 30, Status = EventParticipationStatus.Verified, MissionCompletedAt = _now.AddHours(-5) });
        var pVote1 = _uow.Seed(new EventParticipation { Id = 44, EventId = 1, EventActivityId = voteAct.Id, UserId = parent1.Id, AcceptedRuleVersionId = 30, Status = EventParticipationStatus.Verified });

        _uow.Seed(new Story { Id = 101, AuthorUserId = creator1.Id, IsPublished = true, Title = "C1 High" });
        _uow.Seed(new Story { Id = 102, AuthorUserId = creator1.Id, IsPublished = true, Title = "C1 Low" });
        _uow.Seed(new Story { Id = 103, AuthorUserId = creator2.Id, IsPublished = true, Title = "C2 Mid" });

        // Creator1 has 2 approved submissions (scores 95 and 70); Creator2 has 1 (score 85).
        _uow.Seed(new EventSubmission { Id = 201, EventId = 1, ParticipationId = pC1.Id, EventActivityId = subAct.Id, CreatorUserId = creator1.Id, StoryId = 101, CurrentStoryVersionId = 301, LockedStoryVersionId = 301, Status = EventSubmissionStatus.Approved, SubmittedAt = _now.AddDays(-4) });
        _uow.Seed(new EventSubmission { Id = 202, EventId = 1, ParticipationId = pC1.Id, EventActivityId = subAct.Id, CreatorUserId = creator1.Id, StoryId = 102, CurrentStoryVersionId = 302, LockedStoryVersionId = 302, Status = EventSubmissionStatus.Approved, SubmittedAt = _now.AddDays(-4) });
        _uow.Seed(new EventSubmission { Id = 203, EventId = 1, ParticipationId = pC2.Id, EventActivityId = subAct.Id, CreatorUserId = creator2.Id, StoryId = 103, CurrentStoryVersionId = 303, LockedStoryVersionId = 303, Status = EventSubmissionStatus.Approved, SubmittedAt = _now.AddDays(-4) });

        _uow.Seed(new EventEvaluation { Id = 501, EventSubmissionId = 201, EvaluatorUserId = 1, CriteriaScoresJson = """{"creativity":95,"education":95,"structure":95,"media":95}""", TotalWeightedScore = 95 });
        _uow.Seed(new EventEvaluation { Id = 502, EventSubmissionId = 202, EvaluatorUserId = 1, CriteriaScoresJson = """{"creativity":70,"education":70,"structure":70,"media":70}""", TotalWeightedScore = 70 });
        _uow.Seed(new EventEvaluation { Id = 503, EventSubmissionId = 203, EvaluatorUserId = 1, CriteriaScoresJson = """{"creativity":85,"education":85,"structure":85,"media":85}""", TotalWeightedScore = 85 });

        // Parent1 votes Creator1's story 101 (5 stars) and Creator2's story 103 (3 stars).
        _uow.Seed(new EventVote { Id = 601, EventId = 1, EventSubmissionId = 201, StoryId = 101, StoryVersionId = 301, ParentUserId = parent1.Id, ParticipationId = pVote1.Id, Stars = 5 });
        _uow.Seed(new EventVote { Id = 602, EventId = 1, EventSubmissionId = 203, StoryId = 103, StoryVersionId = 303, ParentUserId = parent1.Id, ParticipationId = pVote1.Id, Stars = 3 });

        var awSub1 = _uow.Seed(new EventAward { Id = 701, EventId = 1, EventActivityId = subAct.Id, ResultSource = EventAwardResultSource.SubmissionScore, TargetAudience = EventAudience.Both, RankOrTier = "Giải Sáng tác", SlotIndex = 1, RewardType = EventRewardType.PhysicalGift, RewardName = "Cúp 1", UnitValueVnd = 0 });
        var awSub2 = _uow.Seed(new EventAward { Id = 702, EventId = 1, EventActivityId = subAct.Id, ResultSource = EventAwardResultSource.SubmissionScore, TargetAudience = EventAudience.Both, RankOrTier = "Giải Sáng tác", SlotIndex = 2, RewardType = EventRewardType.PhysicalGift, RewardName = "Cúp 2", UnitValueVnd = 0 });
        var awVote1 = _uow.Seed(new EventAward { Id = 703, EventId = 1, EventActivityId = voteAct.Id, ResultSource = EventAwardResultSource.VotingAverage, TargetAudience = EventAudience.Creator, RankOrTier = "Yêu thích nhất", SlotIndex = 1, RewardType = EventRewardType.PhysicalGift, RewardName = "Cúp Vote", UnitValueVnd = 0 });
        var awRead1 = _uow.Seed(new EventAward { Id = 704, EventId = 1, EventActivityId = readAct.Id, ResultSource = EventAwardResultSource.MissionCompletion, TargetAudience = EventAudience.Parent, RankOrTier = "Quà Đọc Sớm", SlotIndex = 1, RewardType = EventRewardType.PhysicalGift, RewardName = "Bộ sách", UnitValueVnd = 0 });

        var rewardSvc = new EventRewardService(_uow, _audit.Object);
        var proposed = (await rewardSvc.ProposeAwardsAsync(1, 1)).ToDictionary(a => a.Id);

        // Tier "Giải Sáng tác" takes Creator1's best entry (95) -> slot 1 = Creator1 (pC1), slot 2 = Creator2 (pC2), NOT Creator1's second entry!
        Assert.Equal(pC1.Id, proposed[awSub1.Id].ParticipationId);
        Assert.Equal(pC2.Id, proposed[awSub2.Id].ParticipationId);
        // Same user (Creator1) IS allowed to win in a different tier ("Yêu thích nhất").
        Assert.Equal(pC1.Id, proposed[awVote1.Id].ParticipationId);
        // MissionCompletion picks Parent2 (completed 5h ago) before Parent1 (completed 3h ago).
        Assert.Equal(pRead2.Id, proposed[awRead1.Id].ParticipationId);

        // Approve & freeze, then expire awRead1 and awSub1 to test reallocation.
        await rewardSvc.ApproveAwardsAsync(2, 1);
        awRead1.ClaimDeadlineAt = _now.AddMinutes(-5);
        awSub1.ClaimDeadlineAt = _now.AddMinutes(-5);

        var reallocated = await rewardSvc.ProcessExpiredClaimsAsync(1, 1);
        // awRead1 reallocates to pRead1; awSub1 cannot double-award pC2 (already holds slot 2 of same tier) -> UnclaimedBudgetReturned.
        Assert.Equal(1, reallocated);
        Assert.Contains(_uow.Items<EventAward>(), a => a.ReallocatedFromAwardId == awRead1.Id && a.ParticipationId == pRead1.Id && a.ReallocationRound == 1);
        Assert.Contains(_uow.Items<EventAward>(), a => a.ReallocatedFromAwardId == awSub1.Id && a.Status == EventAwardStatus.UnclaimedBudgetReturned);
    }

    [Fact]
    public void VietnamTime_EndOfDayInclusiveBoundary_Matches165959Utc()
    {
        var vnEndOfDay = new DateTimeOffset(2026, 11, 24, 23, 59, 59, VietnamTime.Offset).UtcDateTime;
        Assert.Equal(new DateTime(2026, 11, 24, 16, 59, 59, DateTimeKind.Utc), vnEndOfDay);
        Assert.Equal(new DateTime(2026, 11, 24), VietnamTime.LocalDate(vnEndOfDay));
        Assert.Equal(new DateTime(2026, 11, 25), VietnamTime.LocalDate(vnEndOfDay.AddSeconds(1)));
    }

    private (EventActivity SubAct, EventParticipation PA, EventParticipation PB, EventParticipation PC) SeedThreeCreatorSubmissionScores(
        int scoreA, int scoreB, int scoreC)
    {
        var cA = _uow.Seed(new UserAccount { Id = 10, Email = "ca@test.local", Role = UserRole.ContentCreator });
        var cB = _uow.Seed(new UserAccount { Id = 11, Email = "cb@test.local", Role = UserRole.ContentCreator });
        var cC = _uow.Seed(new UserAccount { Id = 12, Email = "cc@test.local", Role = UserRole.ContentCreator });
        _uow.Seed(new Event { Id = 1, Title = "Multi Tier", Slug = "multi-tier", MainCategoryId = 1, Status = EventStatus.Published, ConfigVersion = EventConfigVersion.ActivityBased, AssignedModeratorId = 1 });
        var subAct = _uow.Seed(new EventActivity { Id = 20, EventId = 1, ActivityType = EventActivityType.Submission, AllowedAudience = EventAudience.Creator, StartAt = _now.AddDays(-5), EndAt = _now.AddHours(-2), ClaimWindowHours = 24, MaxReallocationRounds = 1 });
        _uow.Seed(new EventRuleVersion { Id = 30, EventId = 1, IsActive = true, RulesText = "Rules" });

        var pA = _uow.Seed(new EventParticipation { Id = 40, EventId = 1, EventActivityId = subAct.Id, UserId = cA.Id, AcceptedRuleVersionId = 30, Status = EventParticipationStatus.Verified });
        var pB = _uow.Seed(new EventParticipation { Id = 41, EventId = 1, EventActivityId = subAct.Id, UserId = cB.Id, AcceptedRuleVersionId = 30, Status = EventParticipationStatus.Verified });
        var pC = _uow.Seed(new EventParticipation { Id = 42, EventId = 1, EventActivityId = subAct.Id, UserId = cC.Id, AcceptedRuleVersionId = 30, Status = EventParticipationStatus.Verified });

        _uow.Seed(new EventSubmission { Id = 201, EventId = 1, ParticipationId = pA.Id, EventActivityId = subAct.Id, CreatorUserId = cA.Id, StoryId = 101, CurrentStoryVersionId = 301, LockedStoryVersionId = 301, Status = EventSubmissionStatus.Approved, SubmittedAt = _now.AddDays(-4) });
        _uow.Seed(new EventSubmission { Id = 202, EventId = 1, ParticipationId = pB.Id, EventActivityId = subAct.Id, CreatorUserId = cB.Id, StoryId = 102, CurrentStoryVersionId = 302, LockedStoryVersionId = 302, Status = EventSubmissionStatus.Approved, SubmittedAt = _now.AddDays(-4) });
        _uow.Seed(new EventSubmission { Id = 203, EventId = 1, ParticipationId = pC.Id, EventActivityId = subAct.Id, CreatorUserId = cC.Id, StoryId = 103, CurrentStoryVersionId = 303, LockedStoryVersionId = 303, Status = EventSubmissionStatus.Approved, SubmittedAt = _now.AddDays(-4) });

        _uow.Seed(new EventEvaluation { Id = 501, EventSubmissionId = 201, EvaluatorUserId = 1, CriteriaScoresJson = $$"""{"creativity":{{scoreA}},"education":{{scoreA}},"structure":{{scoreA}},"media":{{scoreA}}}""", TotalWeightedScore = scoreA });
        _uow.Seed(new EventEvaluation { Id = 502, EventSubmissionId = 202, EvaluatorUserId = 1, CriteriaScoresJson = $$"""{"creativity":{{scoreB}},"education":{{scoreB}},"structure":{{scoreB}},"media":{{scoreB}}}""", TotalWeightedScore = scoreB });
        _uow.Seed(new EventEvaluation { Id = 503, EventSubmissionId = 203, EvaluatorUserId = 1, CriteriaScoresJson = $$"""{"creativity":{{scoreC}},"education":{{scoreC}},"structure":{{scoreC}},"media":{{scoreC}}}""", TotalWeightedScore = scoreC });

        return (subAct, pA, pB, pC);
    }

    [Fact]
    public async Task ActivityTiers_SameSource_AssignDistinctWinnersInTierOrder()
    {
        var (subAct, pA, pB, pC) = SeedThreeCreatorSubmissionScores(90, 80, 70);
        _uow.Seed(new EventAward { Id = 100, EventId = 1, EventActivityId = subAct.Id, ResultSource = EventAwardResultSource.SubmissionScore, TargetAudience = EventAudience.Creator, RankOrTier = "Nhất", SlotIndex = 1, RewardName = "Nhất" });
        _uow.Seed(new EventAward { Id = 101, EventId = 1, EventActivityId = subAct.Id, ResultSource = EventAwardResultSource.SubmissionScore, TargetAudience = EventAudience.Creator, RankOrTier = "Nhì", SlotIndex = 1, RewardName = "Nhì 1" });
        _uow.Seed(new EventAward { Id = 102, EventId = 1, EventActivityId = subAct.Id, ResultSource = EventAwardResultSource.SubmissionScore, TargetAudience = EventAudience.Creator, RankOrTier = "Nhì", SlotIndex = 2, RewardName = "Nhì 2" });

        var proposed = (await new EventRewardService(_uow, _audit.Object).ProposeAwardsAsync(1, 1)).ToDictionary(a => a.Id);

        Assert.Equal(pA.Id, proposed[100].ParticipationId);
        Assert.Equal(pB.Id, proposed[101].ParticipationId);
        Assert.Equal(pC.Id, proposed[102].ParticipationId);
    }

    [Fact]
    public async Task ActivityTiers_OverlappingAudiences_SameSource_NoDuplicateWinner()
    {
        var (act, creatorA, creatorB, parent) = SeedThreeCreatorSubmissionScores(90, 70, 80);
        act.AllowedAudience = EventAudience.Both;
        _uow.Items<UserAccount>().Single(x => x.Id == parent.UserId).Role = UserRole.User;
        _uow.Seed(new EventAward { Id = 100, EventId = 1, EventActivityId = act.Id, ResultSource = EventAwardResultSource.SubmissionScore, TargetAudience = EventAudience.Both, RankOrTier = "Chung" });
        _uow.Seed(new EventAward { Id = 101, EventId = 1, EventActivityId = act.Id, ResultSource = EventAwardResultSource.SubmissionScore, TargetAudience = EventAudience.Creator, RankOrTier = "Creator" });
        _uow.Seed(new EventAward { Id = 102, EventId = 1, EventActivityId = act.Id, ResultSource = EventAwardResultSource.SubmissionScore, TargetAudience = EventAudience.Both, RankOrTier = "Chung tiếp theo" });

        var proposed = (await new EventRewardService(_uow, _audit.Object).ProposeAwardsAsync(1, 1)).ToDictionary(x => x.Id);
        Assert.Equal(creatorA.Id, proposed[100].ParticipationId);
        Assert.Equal(creatorB.Id, proposed[101].ParticipationId);
        Assert.Equal(parent.Id, proposed[102].ParticipationId);
    }

    [Fact]
    public async Task ActivityTiers_FewerCandidatesThanSlots_LeavesLowerTierEmpty()
    {
        var (subAct, pA, pB, pC) = SeedThreeCreatorSubmissionScores(90, 80, 70);
        pC.Status = EventParticipationStatus.VerificationFailed;
        _uow.Seed(new EventAward { Id = 100, EventId = 1, EventActivityId = subAct.Id, ResultSource = EventAwardResultSource.SubmissionScore, TargetAudience = EventAudience.Creator, RankOrTier = "Nhất", SlotIndex = 1, RewardName = "Nhất" });
        _uow.Seed(new EventAward { Id = 101, EventId = 1, EventActivityId = subAct.Id, ResultSource = EventAwardResultSource.SubmissionScore, TargetAudience = EventAudience.Creator, RankOrTier = "Nhì", SlotIndex = 1, RewardName = "Nhì 1" });
        _uow.Seed(new EventAward { Id = 102, EventId = 1, EventActivityId = subAct.Id, ResultSource = EventAwardResultSource.SubmissionScore, TargetAudience = EventAudience.Creator, RankOrTier = "Nhì", SlotIndex = 2, RewardName = "Nhì 2" });

        var proposed = (await new EventRewardService(_uow, _audit.Object).ProposeAwardsAsync(1, 1)).ToDictionary(a => a.Id);

        Assert.Equal(pA.Id, proposed[100].ParticipationId);
        Assert.Equal(pB.Id, proposed[101].ParticipationId);
        Assert.Null(proposed[102].ParticipationId);
    }

    [Fact]
    public async Task ExpiredClaim_Reallocation_SkipsDisqualifiedAndSameSourceWinners()
    {
        var (subAct, pA, pB, pC) = SeedThreeCreatorSubmissionScores(90, 80, 70);
        var ev = _uow.Items<Event>().Single();
        ev.ResultsFinalizedAt = _now.AddHours(-2);
        var rankingCandidates = """[{"ParticipationId":40,"UserId":10},{"ParticipationId":41,"UserId":11},{"ParticipationId":42,"UserId":12}]""";
        ev.FinalAwardRankingSnapshotJson = $$"""[{"ActivityId":20,"Tier":"Nhất","Candidates":{{rankingCandidates}}},{"ActivityId":20,"Tier":"Nhì","Candidates":{{rankingCandidates}}}]""";

        pC.Status = EventParticipationStatus.Disqualified;
        _uow.Seed(new EventAward { Id = 100, EventId = 1, EventActivityId = subAct.Id, ResultSource = EventAwardResultSource.SubmissionScore, TargetAudience = EventAudience.Creator, RankOrTier = "Nhất", SlotIndex = 1, RewardName = "Nhất", ParticipationId = pB.Id, Status = EventAwardStatus.AwaitingClaim, ClaimDeadlineAt = _now.AddHours(10) });
        _uow.Seed(new EventAward { Id = 101, EventId = 1, EventActivityId = subAct.Id, ResultSource = EventAwardResultSource.SubmissionScore, TargetAudience = EventAudience.Creator, RankOrTier = "Nhì", SlotIndex = 1, RewardName = "Nhì", ParticipationId = pA.Id, Status = EventAwardStatus.AwaitingClaim, ClaimDeadlineAt = _now.AddMinutes(-5) });

        var reallocated = await new EventRewardService(_uow, _audit.Object).ProcessExpiredClaimsAsync(1, 1);

        Assert.Equal(0, reallocated);
        Assert.Contains(_uow.Items<EventAward>(), a => a.ReallocatedFromAwardId == 101 && a.Status == EventAwardStatus.UnclaimedBudgetReturned);
    }

    [Fact]
    public async Task Verify_PendingReviewAfterCutoff_MarksReviewExpired()
    {
        _uow.Seed(new Event { Id = 1, Status = EventStatus.Published, ConfigVersion = EventConfigVersion.ActivityBased, AssignedModeratorId = 9 });
        var subAct = _uow.Seed(new EventActivity { Id = 20, EventId = 1, ActivityType = EventActivityType.Submission, EndAt = _now.AddHours(-2), ReviewCutoffAt = _now.AddHours(-1) });
        var p = _uow.Seed(new EventParticipation { Id = 30, EventId = 1, EventActivityId = subAct.Id, UserId = 50, Status = EventParticipationStatus.Registered });
        var sub = _uow.Seed(new EventSubmission { Id = 40, EventId = 1, ParticipationId = p.Id, EventActivityId = subAct.Id, CreatorUserId = 50, StoryId = 100, CurrentStoryVersionId = 200, Status = EventSubmissionStatus.PendingReview });

        await new EventParticipationService(_uow, _audit.Object).VerifyEventParticipationsAsync(9, 1);

        Assert.Equal(EventSubmissionStatus.ReviewExpired, sub.Status);
        Assert.Equal(EventParticipationStatus.VerificationFailed, p.Status);
    }

    [Fact]
    public async Task Appeal_AfterReviewExpired_IsAllowed()
    {
        _uow.Seed(new Event { Id = 1, Status = EventStatus.Published, ConfigVersion = EventConfigVersion.ActivityBased, AssignedModeratorId = 9 });
        var subAct = _uow.Seed(new EventActivity { Id = 20, EventId = 1, ActivityType = EventActivityType.Submission, EndAt = _now.AddHours(-2), ReviewCutoffAt = _now.AddHours(-1) });
        var p = _uow.Seed(new EventParticipation { Id = 30, EventId = 1, EventActivityId = subAct.Id, UserId = 50, Status = EventParticipationStatus.VerificationFailed });
        _uow.Seed(new EventSubmission { Id = 40, EventId = 1, ParticipationId = p.Id, EventActivityId = subAct.Id, CreatorUserId = 50, StoryId = 100, CurrentStoryVersionId = 200, Status = EventSubmissionStatus.ReviewExpired });

        var appeal = await new EventParticipationService(_uow, _audit.Object).CreateAppealAsync(50, 1, new CreateAppealRequestDto
        {
            ParticipationId = p.Id,
            ReasonText = "Bài nộp đúng hạn nhưng chưa được duyệt trước cutoff."
        });

        Assert.Equal(EventAppealStatus.Open, appeal.Status);
    }

    [Fact]
    public async Task Appeal_AfterSafetyRejected_StillForbidden()
    {
        _uow.Seed(new Event { Id = 1, Status = EventStatus.Published, ConfigVersion = EventConfigVersion.ActivityBased, AssignedModeratorId = 9 });
        var subAct = _uow.Seed(new EventActivity { Id = 20, EventId = 1, ActivityType = EventActivityType.Submission, EndAt = _now.AddHours(-2), ReviewCutoffAt = _now.AddHours(-1) });
        var p = _uow.Seed(new EventParticipation { Id = 30, EventId = 1, EventActivityId = subAct.Id, UserId = 50, Status = EventParticipationStatus.VerificationFailed });
        _uow.Seed(new EventSubmission { Id = 40, EventId = 1, ParticipationId = p.Id, EventActivityId = subAct.Id, CreatorUserId = 50, StoryId = 100, CurrentStoryVersionId = 200, Status = EventSubmissionStatus.Rejected });

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            new EventParticipationService(_uow, _audit.Object).CreateAppealAsync(50, 1, new CreateAppealRequestDto
            {
                ParticipationId = p.Id,
                ReasonText = "Xin duyệt lại bài bị từ chối."
            }));
    }
}
