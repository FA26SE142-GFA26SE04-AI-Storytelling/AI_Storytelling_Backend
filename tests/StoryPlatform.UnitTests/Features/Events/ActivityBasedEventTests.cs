using Moq;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Common.Time;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.Events.DTOs;
using StoryPlatform.Application.Features.Events.Services;
using StoryPlatform.Application.Features.Moderation.DTOs;
using StoryPlatform.Application.Features.Moderation.Interfaces;
using StoryPlatform.Application.Features.Notifications.Interfaces;
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
        Assert.Equal("S1", s1.SubmittedTitle);
        var mine = await svc.ListMySubmissionsAsync(creator.Id, 1);
        Assert.Equal(new[] { "S1", "S2" }, mine.Select(x => x.SubmittedTitle).OrderBy(x => x));
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
    public async Task ListVotes_IncludesStoryTitleVoterNameAndReadingTime()
    {
        _uow.Seed(new UserAccount { Id = 11, Username = "parent_a", FullName = "Nguyễn An", Email = "a@test.local", Role = UserRole.User });
        _uow.Seed(new UserAccount { Id = 12, Username = "parent_b", FullName = "", Email = "b@test.local", Role = UserRole.User });
        _uow.Seed(new Story { Id = 100, Title = "Giọt Sương" });
        var done = _now.AddMinutes(-5);
        _uow.Seed(new ReadingSession { Id = 400, StoryId = 100, Status = SessionStatus.Completed, CompletedAt = done });
        _uow.Seed(new EventVote { Id = 1, EventId = 1, StoryId = 100, ParentUserId = 11, ReadingSessionId = 400, Stars = 5, CreatedAt = _now });
        _uow.Seed(new EventVote { Id = 2, EventId = 1, StoryId = 100, ParentUserId = 12, Stars = 3, CreatedAt = _now.AddMinutes(-1) });

        var votes = await new EventParticipationService(_uow, _audit.Object).ListVotesAsync(1);

        Assert.Equal("Giọt Sương", votes[0].StoryTitle);
        Assert.Equal("Nguyễn An", votes[0].VoterName);
        Assert.Equal(done, votes[0].ReadingCompletedAt);
        Assert.Equal("parent_b", votes[1].VoterName);
        Assert.Null(votes[1].ReadingCompletedAt);
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

        // Approve without confirm must throw 409 for ActivityBased events.
        var unconfirmedErr = await Assert.ThrowsAsync<ConflictException>(() => rewardSvc.ApproveAwardsAsync(2, 1));
        Assert.Equal("Moderator chưa xác nhận danh sách người nhận giải.", unconfirmedErr.Message);

        // Confirm & freeze, then approve (AppealHold), then expire 48h appeal window via sweep -> AwaitingClaim, then expire claim to test reallocation.
        await rewardSvc.ConfirmAwardsAsync(1, 1);
        await rewardSvc.ApproveAwardsAsync(2, 1);
        var evObj = _uow.Items<Event>().Single(e => e.Id == 1);
        evObj.ResultsPublishedAt = _now.AddHours(-49);
        await new EventResultSweepService(_uow, _audit.Object).SweepEndedEventsAsync();
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
        _uow.Seed(new Event { Id = 1, Status = EventStatus.Published, ConfigVersion = EventConfigVersion.ActivityBased, AssignedModeratorId = 9, ResultsPublishedAt = _now.AddHours(-1) });
        var subAct = _uow.Seed(new EventActivity { Id = 20, EventId = 1, ActivityType = EventActivityType.Submission, EndAt = _now.AddHours(-2), ReviewCutoffAt = _now.AddHours(-1) });
        _uow.Seed(new EventAward { Id = 99, EventId = 1, EventActivityId = subAct.Id, ResultSource = EventAwardResultSource.SubmissionScore, RankOrTier = "Nhất", SlotIndex = 1, RewardName = "Quà" });
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
        _uow.Seed(new Event { Id = 1, Status = EventStatus.Published, ConfigVersion = EventConfigVersion.ActivityBased, AssignedModeratorId = 9, ResultsPublishedAt = _now.AddHours(-1) });
        var subAct = _uow.Seed(new EventActivity { Id = 20, EventId = 1, ActivityType = EventActivityType.Submission, EndAt = _now.AddHours(-2), ReviewCutoffAt = _now.AddHours(-1) });
        _uow.Seed(new EventAward { Id = 99, EventId = 1, EventActivityId = subAct.Id, ResultSource = EventAwardResultSource.SubmissionScore, RankOrTier = "Nhất", SlotIndex = 1, RewardName = "Quà" });
        var p = _uow.Seed(new EventParticipation { Id = 30, EventId = 1, EventActivityId = subAct.Id, UserId = 50, Status = EventParticipationStatus.VerificationFailed });
        _uow.Seed(new EventSubmission { Id = 40, EventId = 1, ParticipationId = p.Id, EventActivityId = subAct.Id, CreatorUserId = 50, StoryId = 100, CurrentStoryVersionId = 200, Status = EventSubmissionStatus.Rejected });

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            new EventParticipationService(_uow, _audit.Object).CreateAppealAsync(50, 1, new CreateAppealRequestDto
            {
                ParticipationId = p.Id,
                ReasonText = "Xin duyệt lại bài bị từ chối."
            }));
    }

    [Fact]
    public async Task EventResultSweep_AutoVerifiesAndProposesOnce_AndLogsSystemAudit()
    {
        var creator = _uow.Seed(new UserAccount { Id = 10, Email = "creator@test.local", FullName = "Nguyễn Lan", Role = UserRole.ContentCreator });
        _uow.Seed(new Story { Id = 101, Title = "Hành trình nắng mai", AuthorUserId = creator.Id, IsPublished = true });
        _uow.Seed(new Event
        {
            Id = 1,
            Title = "Auto Sweep Event",
            Slug = "auto-sweep",
            MainCategoryId = 1,
            Status = EventStatus.Published,
            ConfigVersion = EventConfigVersion.ActivityBased,
            AssignedModeratorId = 9,
            ClosesAt = _now.AddHours(-2)
        });
        var subAct = _uow.Seed(new EventActivity
        {
            Id = 20,
            EventId = 1,
            ActivityType = EventActivityType.Submission,
            AllowedAudience = EventAudience.Creator,
            StartAt = _now.AddDays(-5),
            EndAt = _now.AddHours(-2),
            ReviewCutoffAt = _now.AddHours(-1),
            ClaimWindowHours = 48
        });
        _uow.Seed(new EventRuleVersion { Id = 30, EventId = 1, IsActive = true, RulesText = "Rules" });
        var part = _uow.Seed(new EventParticipation
        {
            Id = 40,
            EventId = 1,
            EventActivityId = subAct.Id,
            UserId = creator.Id,
            AcceptedRuleVersionId = 30,
            Status = EventParticipationStatus.Registered
        });
        _uow.Seed(new EventSubmission
        {
            Id = 201,
            EventId = 1,
            ParticipationId = part.Id,
            EventActivityId = subAct.Id,
            CreatorUserId = creator.Id,
            StoryId = 101,
            CurrentStoryVersionId = 301,
            LockedStoryVersionId = 301,
            Status = EventSubmissionStatus.Approved,
            SubmittedAt = _now.AddDays(-3)
        });
        _uow.Seed(new EventEvaluation
        {
            Id = 501,
            EventSubmissionId = 201,
            EvaluatorUserId = 9,
            CriteriaScoresJson = """{"creativity":90,"education":90,"structure":90,"media":90}""",
            TotalWeightedScore = 90
        });
        var award = _uow.Seed(new EventAward
        {
            Id = 700,
            EventId = 1,
            EventActivityId = subAct.Id,
            ResultSource = EventAwardResultSource.SubmissionScore,
            TargetAudience = EventAudience.Creator,
            RankOrTier = "Giải Nhất",
            SlotIndex = 1,
            RewardName = "Quà Nhất"
        });

        var sweep = new EventResultSweepService(_uow, _audit.Object);
        var firstRun = await sweep.SweepEndedEventsAsync();
        Assert.Equal(1, firstRun);
        Assert.Equal(EventParticipationStatus.Verified, part.Status);
        Assert.Equal(part.Id, award.ParticipationId);
        Assert.NotNull(part.VerifiedAt);

        _audit.Verify(a => a.LogAsync(null, "EventParticipationsVerified", nameof(Event), 1, null, It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
        _audit.Verify(a => a.LogAsync(null, "EventAwardsProposed", nameof(Event), 1, null, It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);

        // Chạy lại ngay: không còn hồ sơ chờ xác minh nên không chạy lặp.
        var secondRun = await sweep.SweepEndedEventsAsync();
        Assert.Equal(0, secondRun);
    }

    [Fact]
    public async Task ManualAssignAward_PreservedOnRecalculateAndAppeal_ValidatesRules_AndListsRankedCandidates()
    {
        var (subAct, pA, pB, pC) = SeedThreeCreatorSubmissionScores(90, 80, 70);
        _uow.Items<UserAccount>().Single(u => u.Id == pA.UserId).FullName = "Tác giả A";
        _uow.Items<UserAccount>().Single(u => u.Id == pB.UserId).FullName = "Tác giả B";
        _uow.Items<UserAccount>().Single(u => u.Id == pC.UserId).FullName = "Tác giả C";
        _uow.Seed(new Story { Id = 101, Title = "Truyện A", AuthorUserId = pA.UserId, IsPublished = true });
        _uow.Seed(new Story { Id = 102, Title = "Truyện B", AuthorUserId = pB.UserId, IsPublished = true });
        _uow.Seed(new Story { Id = 103, Title = "Truyện C", AuthorUserId = pC.UserId, IsPublished = true });

        var cD = _uow.Seed(new UserAccount { Id = 13, Email = "cd@test.local", FullName = "Tác giả D", Role = UserRole.ContentCreator });
        var pD = _uow.Seed(new EventParticipation { Id = 43, EventId = 1, EventActivityId = subAct.Id, UserId = cD.Id, AcceptedRuleVersionId = 30, Status = EventParticipationStatus.VerificationFailed });
        _uow.Seed(new EventSubmission { Id = 204, EventId = 1, ParticipationId = pD.Id, EventActivityId = subAct.Id, CreatorUserId = cD.Id, StoryId = 104, CurrentStoryVersionId = 304, LockedStoryVersionId = 304, Status = EventSubmissionStatus.Approved, SubmittedAt = _now.AddDays(-4) });
        _uow.Seed(new Story { Id = 104, Title = "Truyện D", AuthorUserId = cD.Id, IsPublished = true });
        _uow.Seed(new EventEvaluation { Id = 504, EventSubmissionId = 204, EvaluatorUserId = 1, CriteriaScoresJson = """{"creativity":99,"education":99,"structure":99,"media":99}""", TotalWeightedScore = 99 });

        var aw1 = _uow.Seed(new EventAward { Id = 100, EventId = 1, EventActivityId = subAct.Id, ResultSource = EventAwardResultSource.SubmissionScore, TargetAudience = EventAudience.Creator, RankOrTier = "Nhất", SlotIndex = 1, RewardName = "Nhất" });
        var aw2 = _uow.Seed(new EventAward { Id = 101, EventId = 1, EventActivityId = subAct.Id, ResultSource = EventAwardResultSource.SubmissionScore, TargetAudience = EventAudience.Creator, RankOrTier = "Nhì", SlotIndex = 1, RewardName = "Nhì" });

        var rewardSvc = new EventRewardService(_uow, _audit.Object);
        var proposed = (await rewardSvc.ProposeAwardsAsync(1, 1)).ToDictionary(a => a.Id);
        Assert.Equal(pA.Id, proposed[100].ParticipationId);
        Assert.Equal("Tác giả A", proposed[100].WinnerName);
        Assert.Equal("Truyện A", proposed[100].WinnerStoryTitle);
        Assert.Equal(pB.Id, proposed[101].ParticipationId);

        // Danh sách ứng viên chỉ gồm Verified, xếp hạng đúng thứ tự hệ thống (A -> B -> C).
        var candidates = await rewardSvc.GetAwardCandidatesAsync(1, aw1.Id);
        Assert.Equal(new[] { pA.Id, pB.Id, pC.Id }, candidates.Select(c => c.ParticipationId).ToArray());
        Assert.Equal("Tác giả A", candidates[0].DisplayName);
        Assert.Equal("Truyện A", candidates[0].StoryTitle);
        Assert.Equal(aw1.Id, candidates[0].CurrentAwardId);
        Assert.Equal(aw2.Id, candidates[1].CurrentAwardId);

        // Thiếu lý do -> lỗi.
        await Assert.ThrowsAsync<BadRequestException>(() =>
            rewardSvc.ManualAssignAwardAsync(1, aw1.Id, new ManualAssignAwardRequestDto { ParticipationId = pC.Id, Reason = "   " }));
        // Gán người chưa Verified -> 409.
        await Assert.ThrowsAsync<ConflictException>(() =>
            rewardSvc.ManualAssignAwardAsync(1, aw1.Id, new ManualAssignAwardRequestDto { ParticipationId = pD.Id, Reason = "Thử gán người chưa đạt" }));
        // Gán người đang giữ suất khác cùng nguồn -> 409.
        await Assert.ThrowsAsync<ConflictException>(() =>
            rewardSvc.ManualAssignAwardAsync(1, aw1.Id, new ManualAssignAwardRequestDto { ParticipationId = pB.Id, Reason = "Trùng nguồn" }));

        // Chỉnh tay suất Nhất -> pC, và bỏ trống suất Nhì.
        var edited1 = await rewardSvc.ManualAssignAwardAsync(1, aw1.Id, new ManualAssignAwardRequestDto
        {
            ParticipationId = pC.Id,
            Reason = "Điều chỉnh theo biên bản hội đồng."
        });
        Assert.Equal(pC.Id, edited1.ParticipationId);
        Assert.True(edited1.IsManualAssignment);
        Assert.Equal("Điều chỉnh theo biên bản hội đồng.", edited1.ManualAssignmentNote);
        Assert.Equal("Tác giả C", edited1.WinnerName);
        Assert.Equal("Truyện C", edited1.WinnerStoryTitle);

        var cleared2 = await rewardSvc.ManualAssignAwardAsync(1, aw2.Id, new ManualAssignAwardRequestDto
        {
            ParticipationId = null,
            Reason = "Bỏ trống suất theo thống nhất BTC."
        });
        Assert.Null(cleared2.ParticipationId);
        Assert.True(cleared2.IsManualAssignment);

        // Thêm suất tự động thứ 3 -> tính lại danh sách không ghi đè suất chỉnh tay và không gán trùng pC.
        var aw3 = _uow.Seed(new EventAward { Id = 102, EventId = 1, EventActivityId = subAct.Id, ResultSource = EventAwardResultSource.SubmissionScore, TargetAudience = EventAudience.Creator, RankOrTier = "Ba", SlotIndex = 1, RewardName = "Ba" });
        var recalculated = (await rewardSvc.ProposeAwardsAsync(1, 1)).ToDictionary(a => a.Id);
        Assert.Equal(pC.Id, recalculated[aw1.Id].ParticipationId);
        Assert.Null(recalculated[aw2.Id].ParticipationId);
        Assert.Equal(pA.Id, recalculated[aw3.Id].ParticipationId);

        // Khiếu nại của pD (điểm cao nhất 99) được chấp nhận -> tính lại chỉ cập nhật suất tự động aw3, giữ nguyên aw1 & aw2 đã chỉnh tay.
        var appeal = _uow.Seed(new EventAppeal { Id = 801, EventId = 1, ParticipationId = pD.Id, AppellantUserId = cD.Id, ReasonText = "Khôi phục kết quả", Status = EventAppealStatus.Open });
        await new EventParticipationService(_uow, _audit.Object).DecideAppealAsync(1, appeal.Id, new DecideAppealRequestDto
        {
            Decision = EventAppealStatus.Accepted,
            ResolutionNote = "Đã kiểm tra hợp lệ."
        });
        Assert.Equal(EventParticipationStatus.Verified, pD.Status);
        Assert.Equal(pC.Id, aw1.ParticipationId);
        Assert.Null(aw2.ParticipationId);
        Assert.Equal(pD.Id, aw3.ParticipationId);
    }

    [Fact]
    public async Task ConfirmAwards_DoesNotWaitForAppealWindow_AndFreezesManualEdits()
    {
        var (subAct, pA, pB, pC) = SeedThreeCreatorSubmissionScores(90, 80, 70);
        _uow.Seed(new EventParticipation
        {
            Id = 44,
            EventId = 1,
            EventActivityId = subAct.Id,
            UserId = 14,
            AcceptedRuleVersionId = 30,
            Status = EventParticipationStatus.VerificationFailed,
            VerifiedAt = _now.AddHours(-1)
        });
        var aw1 = _uow.Seed(new EventAward { Id = 100, EventId = 1, EventActivityId = subAct.Id, ResultSource = EventAwardResultSource.SubmissionScore, TargetAudience = EventAudience.Creator, RankOrTier = "Nhất", SlotIndex = 1, RewardName = "Nhất" });

        var rewardSvc = new EventRewardService(_uow, _audit.Object);
        await rewardSvc.ProposeAwardsAsync(1, 1);
        await rewardSvc.ManualAssignAwardAsync(1, aw1.Id, new ManualAssignAwardRequestDto { ParticipationId = pC.Id, Reason = "Chọn C" });

        // Không cần chờ 48h trước khi xác nhận danh sách.
        await rewardSvc.ConfirmAwardsAsync(1, 1);

        var ev = _uow.Items<Event>().Single();
        Assert.NotNull(ev.ResultsFinalizedAt);
        Assert.False(string.IsNullOrWhiteSpace(ev.FinalAwardRankingSnapshotJson));

        await Assert.ThrowsAsync<ConflictException>(() => rewardSvc.ProposeAwardsAsync(1, 1));
        await Assert.ThrowsAsync<ConflictException>(() => rewardSvc.ManualAssignAwardAsync(1, aw1.Id, new ManualAssignAwardRequestDto { ParticipationId = pB.Id, Reason = "Sửa sau chốt" }));

        var approved = await rewardSvc.ApproveAwardsAsync(2, 1);
        Assert.Equal(pC.Id, Assert.Single(approved).ParticipationId);
        Assert.Equal(EventAwardStatus.AppealHold, aw1.Status);
        Assert.Null(aw1.ClaimDeadlineAt);
        Assert.NotNull(ev.ResultsPublishedAt);
    }

    [Fact]
    public async Task ConfirmAwards_WithoutPropose_RecalculatesSlotsBeforeFreezing()
    {
        var (subAct, pA, _, pC) = SeedThreeCreatorSubmissionScores(90, 80, 70);
        var aw1 = _uow.Seed(new EventAward { Id = 100, EventId = 1, EventActivityId = subAct.Id, ResultSource = EventAwardResultSource.SubmissionScore, TargetAudience = EventAudience.Creator, RankOrTier = "Nhất", SlotIndex = 1, RewardName = "Nhất" });
        var aw2 = _uow.Seed(new EventAward { Id = 101, EventId = 1, EventActivityId = subAct.Id, ResultSource = EventAwardResultSource.SubmissionScore, TargetAudience = EventAudience.Creator, RankOrTier = "Nhì", SlotIndex = 1, RewardName = "Nhì", ParticipationId = pC.Id, ManuallyAssignedAt = _now.AddHours(-1), ManualAssignmentNote = "Chọn C" });

        // Moderator bấm "Xác nhận" mà chưa bấm "Lập danh sách": suất tự động vẫn được tính, suất chọn tay giữ nguyên.
        await new EventRewardService(_uow, _audit.Object).ConfirmAwardsAsync(1, 1);

        Assert.Equal(pA.Id, aw1.ParticipationId);
        Assert.Equal(pC.Id, aw2.ParticipationId);
        Assert.NotNull(_uow.Items<Event>().Single().ResultsFinalizedAt);
    }

    [Fact]
    public async Task ConfirmAndApprove_RecheckManualRecipients()
    {
        var (subAct, pA, pB, pC) = SeedThreeCreatorSubmissionScores(90, 80, 70);
        var award = _uow.Seed(new EventAward
        {
            Id = 100, EventId = 1, EventActivityId = subAct.Id, ResultSource = EventAwardResultSource.SubmissionScore,
            RankOrTier = "Nhất", SlotIndex = 1, ParticipationId = pC.Id, ManuallyAssignedAt = _now
        });
        var store = new EventRewardService(_uow, _audit.Object);
        pC.Status = EventParticipationStatus.VerificationFailed;
        await store.ConfirmAwardsAsync(1, 1);
        Assert.Equal(pA.Id, award.ParticipationId);
        Assert.Null(award.ManuallyAssignedAt);

        award.ParticipationId = pB.Id;
        award.ManuallyAssignedAt = _now;
        pB.Status = EventParticipationStatus.VerificationFailed;
        Assert.Empty(await store.ApproveAwardsAsync(2, 1));
        Assert.Null(award.ParticipationId);
        Assert.Equal(EventAwardStatus.AwardProposed, award.Status);
    }

    [Fact]
    public async Task Approve_NotificationStorageFailure_RollsBackAndReportsRetryableError()
    {
        SeedThreeCreatorSubmissionScores(90, 80, 70);
        _uow.Items<Event>().Single().ResultsFinalizedAt = _now.AddHours(-1);
        var notices = new Mock<INotificationService>();
        notices.Setup(n => n.CreateStoredAsync(It.IsAny<int>(), NotificationType.EventResultPublished,
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Notification storage failure"));

        var service = new EventRewardService(_uow, _audit.Object, notices.Object);
        var error = await Assert.ThrowsAnyAsync<AppException>(() => service.ApproveAwardsAsync(2, 1));
        Assert.Equal(503, error.StatusCode);
        Assert.Equal("EVENT_APPROVAL_NOTIFICATION_UNAVAILABLE", error.ErrorCode);
        Assert.Contains("rollback", _uow.Calls);
        Assert.DoesNotContain("commit", _uow.Calls);
    }

    [Fact]
    public async Task ApproveHistoricallyPublishedEvent_StoresClaimNoticeForReplacement()
    {
        var (subAct, pA, pB, _) = SeedThreeCreatorSubmissionScores(90, 80, 70);
        var ev = _uow.Items<Event>().Single();
        ev.ResultsFinalizedAt = _now.AddDays(-4);
        var expired = _uow.Seed(new EventAward
        {
            Id = 100, EventId = 1, EventActivityId = subAct.Id, RankOrTier = "Nhất", RewardName = "Quà", SlotIndex = 1,
            ParticipationId = pA.Id, Status = EventAwardStatus.Expired, ApprovedByAdminId = 2
        });
        var replacement = _uow.Seed(new EventAward
        {
            Id = 101, EventId = 1, EventActivityId = subAct.Id, RankOrTier = "Nhất", RewardName = "Quà", SlotIndex = 1,
            ParticipationId = pB.Id, Status = EventAwardStatus.AwardProposed, ReallocatedFromAwardId = expired.Id
        });
        var notices = new Mock<INotificationService>();
        var rewardService = new EventRewardService(_uow, _audit.Object, notices.Object);

        await rewardService.ApproveAwardsAsync(2, 1);
        Assert.Empty(await rewardService.ApproveAwardsAsync(2, 1));
        Assert.Equal(EventAwardStatus.AwaitingClaim, replacement.Status);
        Assert.NotNull(replacement.ClaimDeadlineAt);
        notices.Verify(n => n.CreateStoredAsync(pB.UserId, NotificationType.EventAwardApproved,
            It.Is<string>(payload => payload.Contains("\"awardId\":101")), It.IsAny<CancellationToken>()), Times.Once);
        notices.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Approve_PublishesResults_HoldsAwards_AndNotifiesAllParticipants()
    {
        var (subAct, pA, pB, pC) = SeedThreeCreatorSubmissionScores(90, 80, 70);
        var aw1 = _uow.Seed(new EventAward { Id = 100, EventId = 1, EventActivityId = subAct.Id, ResultSource = EventAwardResultSource.SubmissionScore, TargetAudience = EventAudience.Creator, RankOrTier = "Nhất", SlotIndex = 1, RewardName = "Quà Nhất" });
        var aw2 = _uow.Seed(new EventAward { Id = 101, EventId = 1, EventActivityId = subAct.Id, ResultSource = EventAwardResultSource.SubmissionScore, TargetAudience = EventAudience.Creator, RankOrTier = "Nhì", SlotIndex = 1, RewardName = "Quà Nhì" });

        var notif = new Mock<INotificationService>();
        var rewardSvc = new EventRewardService(_uow, _audit.Object, notif.Object);
        await rewardSvc.ProposeAwardsAsync(1, 1);
        await rewardSvc.ConfirmAwardsAsync(1, 1);
        Assert.Empty(await rewardSvc.GetMyRewardsAsync(pA.UserId));
        var approved = await rewardSvc.ApproveAwardsAsync(2, 1);
        Assert.Empty(await rewardSvc.ApproveAwardsAsync(2, 1));

        var ev = _uow.Items<Event>().Single();
        Assert.NotNull(ev.ResultsPublishedAt);
        Assert.Equal(2, approved.Count);
        Assert.All(new[] { aw1, aw2 }, a =>
        {
            Assert.Equal(EventAwardStatus.AppealHold, a.Status);
            Assert.Null(a.ClaimDeadlineAt);
        });

        // Gửi EventResultPublished đúng 1 lần cho cả 3 người tham gia (2 có giải + 1 không có giải).
        foreach (var uid in new[] { pA.UserId, pB.UserId, pC.UserId })
        {
            notif.Verify(n => n.CreateStoredAsync(uid, NotificationType.EventResultPublished, It.Is<string>(s => s.Contains("\"eventId\":1")), It.IsAny<CancellationToken>()), Times.Once);
        }
        notif.Verify(n => n.CreateAsync(It.IsAny<int>(), NotificationType.EventAwardApproved, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

        notif.Invocations.Clear();
        aw1.Status = EventAwardStatus.Expired;
        _uow.Seed(new EventAward
        {
            Id = 102, EventId = 1, EventActivityId = subAct.Id, ResultSource = aw1.ResultSource,
            RankOrTier = aw1.RankOrTier, SlotIndex = aw1.SlotIndex, RewardName = aw1.RewardName,
            ParticipationId = pC.Id, ReallocatedFromAwardId = aw1.Id, Status = EventAwardStatus.AwardProposed
        });
        Assert.Single(await rewardSvc.ApproveAwardsAsync(2, 1));
        notif.Verify(n => n.CreateStoredAsync(pC.UserId, NotificationType.EventResultPublished, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        notif.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Claim_DuringAppealHold_IsRejected()
    {
        var (subAct, pA, _, _) = SeedThreeCreatorSubmissionScores(90, 80, 70);
        var ev = _uow.Items<Event>().Single();
        ev.ResultsFinalizedAt = _now.AddHours(-2);
        ev.ResultsPublishedAt = _now.AddHours(-1);
        var aw = _uow.Seed(new EventAward
        {
            Id = 100, EventId = 1, EventActivityId = subAct.Id, ResultSource = EventAwardResultSource.SubmissionScore,
            TargetAudience = EventAudience.Creator, RankOrTier = "Nhất", SlotIndex = 1,
            RewardType = EventRewardType.PlatformCredits, RewardName = "Lượt AI", CreditGrantAmount = 3,
            ParticipationId = pA.Id, Status = EventAwardStatus.AppealHold
        });

        var err = await Assert.ThrowsAsync<ConflictException>(() =>
            new EventRewardService(_uow, _audit.Object).ClaimRewardAsync(pA.UserId, aw.Id, new ClaimRewardRequestDto()));
        Assert.Contains("sau khi hết hạn khiếu nại", err.Message);
    }

    [Fact]
    public async Task Claim_MissionAward_StoresShowChildNames()
    {
        var parent = _uow.Seed(new UserAccount { Id = 20, Email = "p@test.local", Role = UserRole.User });
        _uow.Seed(new Event { Id = 1, Title = "Đọc sách", Slug = "doc", MainCategoryId = 1, Status = EventStatus.Published, ConfigVersion = EventConfigVersion.ActivityBased, AssignedModeratorId = 1 });
        var readAct = _uow.Seed(new EventActivity { Id = 21, EventId = 1, ActivityType = EventActivityType.ReadingMissions, AllowedAudience = EventAudience.Parent, StartAt = _now.AddDays(-5), EndAt = _now.AddHours(-2) });
        var part = _uow.Seed(new EventParticipation { Id = 45, EventId = 1, EventActivityId = readAct.Id, UserId = parent.Id, Status = EventParticipationStatus.Verified });
        var aw = _uow.Seed(new EventAward
        {
            Id = 105, EventId = 1, EventActivityId = readAct.Id, ResultSource = EventAwardResultSource.MissionCompletion,
            TargetAudience = EventAudience.Parent, RankOrTier = "Hoàn thành", SlotIndex = 1,
            RewardType = EventRewardType.PlatformCredits, RewardName = "Lượt AI", CreditGrantAmount = 2,
            ParticipationId = part.Id, Status = EventAwardStatus.AwaitingClaim, ClaimDeadlineAt = _now.AddHours(24)
        });

        await new EventRewardService(_uow, _audit.Object).ClaimRewardAsync(parent.Id, aw.Id, new ClaimRewardRequestDto { ShowChildNames = true });
        Assert.True(aw.ShowChildNames);
        Assert.Equal(EventAwardStatus.Fulfilled, aw.Status);
    }

    [Fact]
    public async Task EventDetail_ExposesWinnerNameOnlyToOperators_AndPublishedAppealDeadlineToAll()
    {
        var (subAct, pA, _, _) = SeedThreeCreatorSubmissionScores(90, 80, 70);
        _uow.Items<UserAccount>().Single(u => u.Id == pA.UserId).FullName = "Nguyễn Văn An";
        _uow.Seed(new Story { Id = 101, Title = "Truyện Đạt Giải", AuthorUserId = pA.UserId, IsPublished = true });
        var ev = _uow.Items<Event>().Single();
        ev.ResultsFinalizedAt = _now.AddHours(-3);
        ev.ResultsPublishedAt = _now.AddHours(-2);
        _uow.Seed(new EventAward
        {
            Id = 100,
            EventId = 1,
            EventActivityId = subAct.Id,
            ResultSource = EventAwardResultSource.SubmissionScore,
            TargetAudience = EventAudience.Creator,
            RankOrTier = "Nhất",
            SlotIndex = 1,
            RewardName = "Nhất",
            ParticipationId = pA.Id,
            Status = EventAwardStatus.AppealHold,
            ManualAssignmentNote = "Lý do nội bộ",
            ManuallyAssignedByUserId = 1,
            ManuallyAssignedAt = _now.AddMinutes(-5)
        });

        var mgmt = new EventManagementService(_uow, _audit.Object);

        // Lead Moderator (user 1) & Admin (user 2) thấy tên người nhận, tên truyện, lý do chỉnh tay, ResultsPublishedAt và AppealWindowEndsAt.
        var modDetail = await mgmt.GetEventDetailAsync(1, includeUnpublished: true, currentUserId: 1, currentUserRole: UserRole.Moderator);
        Assert.Equal("Nguyễn Văn An", modDetail.Awards[0].WinnerName);
        Assert.Equal("Truyện Đạt Giải", modDetail.Awards[0].WinnerStoryTitle);
        Assert.True(modDetail.Awards[0].IsManualAssignment);
        Assert.Equal("Lý do nội bộ", modDetail.Awards[0].ManualAssignmentNote);
        Assert.Equal(ev.ResultsPublishedAt, modDetail.ResultsPublishedAt);
        Assert.Equal(ev.ResultsPublishedAt!.Value.AddHours(48), modDetail.AppealWindowEndsAt);

        var adminDetail = await mgmt.GetEventDetailAsync(1, includeUnpublished: true, currentUserId: 2, currentUserRole: UserRole.Administrator);
        Assert.Equal("Nguyễn Văn An", adminDetail.Awards[0].WinnerName);
        Assert.Equal(ev.ResultsPublishedAt!.Value.AddHours(48), adminDetail.AppealWindowEndsAt);

        // Người tham gia thấy ResultsPublishedAt và AppealWindowEndsAt, KHÔNG thấy trường vận hành của giải.
        var participantDetail = await mgmt.GetEventDetailAsync(1, includeUnpublished: false, currentUserId: pA.UserId, currentUserRole: UserRole.ContentCreator);
        Assert.Null(participantDetail.Awards[0].WinnerName);
        Assert.Null(participantDetail.Awards[0].WinnerStoryTitle);
        Assert.False(participantDetail.Awards[0].IsManualAssignment);
        Assert.Null(participantDetail.Awards[0].ManualAssignmentNote);
        Assert.Equal(ev.ResultsPublishedAt, participantDetail.ResultsPublishedAt);
        Assert.Equal(ev.ResultsPublishedAt!.Value.AddHours(48), participantDetail.AppealWindowEndsAt);

        var otherModDetail = await mgmt.GetEventDetailAsync(1, includeUnpublished: true, currentUserId: 99, currentUserRole: UserRole.Moderator);
        Assert.Null(otherModDetail.Awards[0].WinnerName);
        Assert.Null(otherModDetail.Awards[0].WinnerStoryTitle);
    }

    [Fact]
    public async Task PostPublicationAppeals_ValidatesWindowWinnerKind_AndNotifiesOnAccept()
    {
        var (subAct, pA, pB, pC) = SeedThreeCreatorSubmissionScores(90, 80, 70);
        var ev = _uow.Items<Event>().Single();
        var aw1 = _uow.Seed(new EventAward
        {
            Id = 100, EventId = 1, EventActivityId = subAct.Id, ResultSource = EventAwardResultSource.SubmissionScore,
            TargetAudience = EventAudience.Creator, RankOrTier = "Giải Nhất", SlotIndex = 1, RewardName = "Quà Nhất",
            ParticipationId = pA.Id, Status = EventAwardStatus.AppealHold
        });
        var notif = new Mock<INotificationService>();
        var partSvc = new EventParticipationService(_uow, _audit.Object, notifications: notif.Object);

        // Chưa công bố -> 409.
        await Assert.ThrowsAsync<ConflictException>(() =>
            partSvc.CreateAppealAsync(pB.UserId, 1, new CreateAppealRequestDto { ParticipationId = pB.Id, Kind = EventAppealKind.Ranking, ReasonText = "Chưa công bố" }));

        // Quá 48h sau công bố -> 409.
        ev.ResultsPublishedAt = _now.AddHours(-49);
        await Assert.ThrowsAsync<ConflictException>(() =>
            partSvc.CreateAppealAsync(pB.UserId, 1, new CreateAppealRequestDto { ParticipationId = pB.Id, Kind = EventAppealKind.Ranking, ReasonText = "Hết hạn" }));

        // Trong hạn 48h: người đang giữ giải tự khiếu nại -> 409.
        ev.ResultsPublishedAt = _now.AddHours(-1);
        await Assert.ThrowsAsync<ConflictException>(() =>
            partSvc.CreateAppealAsync(pA.UserId, 1, new CreateAppealRequestDto { ParticipationId = pA.Id, Kind = EventAppealKind.Ranking, ReasonText = "Tự khiếu nại" }));

        // Loại Verification nhưng hồ sơ đang Verified -> 409.
        await Assert.ThrowsAsync<ConflictException>(() =>
            partSvc.CreateAppealAsync(pB.UserId, 1, new CreateAppealRequestDto { ParticipationId = pB.Id, Kind = EventAppealKind.Verification, ReasonText = "Sai loại" }));

        // Loại Ranking cho hồ sơ Verified không giữ giải -> thành công + trả TargetAwardLabel.
        var rankingAppeal = await partSvc.CreateAppealAsync(pB.UserId, 1, new CreateAppealRequestDto
        {
            ParticipationId = pB.Id,
            Kind = EventAppealKind.Ranking,
            TargetAwardId = aw1.Id,
            ReasonText = "Đề nghị xem lại điểm tiêu chí sáng tạo."
        });
        Assert.Equal(EventAppealKind.Ranking, rankingAppeal.Kind);
        Assert.Equal(aw1.Id, rankingAppeal.TargetAwardId);
        Assert.Equal("Giải Nhất · Suất #1", rankingAppeal.TargetAwardLabel);

        // Khiếu nại xếp hạng phải được chấp nhận cùng thao tác thu hồi suất.
        ev.ResultsFinalizedAt = _now.AddHours(-2);
        await Assert.ThrowsAsync<ConflictException>(() => partSvc.DecideAppealAsync(1, rankingAppeal.Id, new DecideAppealRequestDto
        {
            Decision = EventAppealStatus.Accepted,
            ResolutionNote = "Đã kiểm tra lại phiếu chấm."
        }));
        Assert.Equal(EventAppealStatus.Open, _uow.Items<EventAppeal>().Single().Status);
        Assert.Equal(EventAwardStatus.AppealHold, aw1.Status);
        var replacement = await new EventRewardService(_uow, _audit.Object, notif.Object).RevokeAwardAsync(1, aw1.Id, new RevokeAwardRequestDto
        {
            AppealId = rankingAppeal.Id, ReplacementParticipationId = pB.Id, Reason = "Đã kiểm tra lại phiếu chấm."
        });
        Assert.Equal(pB.Id, replacement.ParticipationId);
        Assert.Equal(EventAwardStatus.Revoked, aw1.Status);
        Assert.Equal(EventAppealStatus.Accepted, _uow.Items<EventAppeal>().Single().Status);
        notif.Verify(n => n.CreateAsync(pB.UserId, NotificationType.EventAppealAccepted,
            It.Is<string>(s => s != null &&
                System.Text.Json.JsonDocument.Parse(s).RootElement.GetProperty("eventId").GetInt32() == 1 &&
                System.Text.Json.JsonDocument.Parse(s).RootElement.GetProperty("resolutionNote").GetString() == "Đã kiểm tra lại phiếu chấm."),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RevokeAward_WithAndWithoutReplacement_ValidatesWindowSameSourceRoleAndReason()
    {
        var (subAct, pA, pB, pC) = SeedThreeCreatorSubmissionScores(90, 80, 70);
        var ev = _uow.Items<Event>().Single();
        ev.ResultsFinalizedAt = _now.AddHours(-2);
        ev.ResultsPublishedAt = _now.AddHours(-1);
        _uow.Seed(new EventModeratorAssignment { Id = 1, EventId = 1, ModeratorId = 99 }); // Assistant moderator

        var aw1 = _uow.Seed(new EventAward
        {
            Id = 100, EventId = 1, EventActivityId = subAct.Id, ResultSource = EventAwardResultSource.SubmissionScore,
            TargetAudience = EventAudience.Creator, RankOrTier = "Giải Nhất", SlotIndex = 1, RewardName = "Quà Nhất",
            ParticipationId = pA.Id, Status = EventAwardStatus.AppealHold
        });
        var aw2 = _uow.Seed(new EventAward
        {
            Id = 101, EventId = 1, EventActivityId = subAct.Id, ResultSource = EventAwardResultSource.SubmissionScore,
            TargetAudience = EventAudience.Creator, RankOrTier = "Giải Nhì", SlotIndex = 1, RewardName = "Quà Nhì",
            ParticipationId = pB.Id, Status = EventAwardStatus.AppealHold
        });

        var notif = new Mock<INotificationService>();
        var rewardSvc = new EventRewardService(_uow, _audit.Object, notif.Object);

        // Assistant moderator bị chặn (chỉ Moderator phụ trách chính).
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            rewardSvc.RevokeAwardAsync(99, aw1.Id, new RevokeAwardRequestDto { Reason = "Thử thu hồi" }));

        // Lý do rỗng hoặc > 500 ký tự -> 400.
        await Assert.ThrowsAsync<BadRequestException>(() =>
            rewardSvc.RevokeAwardAsync(1, aw1.Id, new RevokeAwardRequestDto { Reason = "   " }));
        await Assert.ThrowsAsync<BadRequestException>(() =>
            rewardSvc.RevokeAwardAsync(1, aw1.Id, new RevokeAwardRequestDto { Reason = new string('x', 501) }));

        // Người thay thế đang giữ suất khác cùng nguồn (pB giữ aw2) -> 409.
        await Assert.ThrowsAsync<ConflictException>(() =>
            rewardSvc.RevokeAwardAsync(1, aw1.Id, new RevokeAwardRequestDto { Reason = "Đổi sang B", ReplacementParticipationId = pB.Id }));

        var wrongAppeal = _uow.Seed(new EventAppeal
        {
            EventId = 1, ParticipationId = pC.Id, AppellantUserId = pC.UserId,
            Kind = EventAppealKind.Ranking, TargetAwardId = aw2.Id, Status = EventAppealStatus.Open
        });
        await Assert.ThrowsAsync<ConflictException>(() =>
            rewardSvc.RevokeAwardAsync(1, aw1.Id, new RevokeAwardRequestDto { Reason = "Sai suất", AppealId = wrongAppeal.Id }));
        Assert.Equal(EventAppealStatus.Open, wrongAppeal.Status);
        Assert.Equal(EventAwardStatus.AppealHold, aw1.Status);
        wrongAppeal.Status = EventAppealStatus.Rejected;

        // Thu hồi kèm người thay thế hợp lệ (pC) -> aw1 Revoked, tạo suất mới AppealHold cho pC, gửi thông báo cả 2.
        var replaced = await rewardSvc.RevokeAwardAsync(1, aw1.Id, new RevokeAwardRequestDto
        {
            Reason = "Bài dự thi vi phạm quy chế sau khiếu nại.",
            ReplacementParticipationId = pC.Id
        });
        Assert.Equal(EventAwardStatus.Revoked, aw1.Status);
        Assert.Equal("Bài dự thi vi phạm quy chế sau khiếu nại.", aw1.RevokedReason);
        Assert.Equal(1, aw1.RevokedByUserId);
        Assert.NotNull(aw1.RevokedAt);
        Assert.NotEqual(aw1.Id, replaced.Id);
        Assert.Equal(EventAwardStatus.AppealHold, replaced.Status);
        Assert.Equal(pC.Id, replaced.ParticipationId);
        Assert.Contains(_uow.Items<EventAward>(), a => a.Id == replaced.Id && a.ReallocatedFromAwardId == aw1.Id);
        notif.Verify(n => n.CreateAsync(pA.UserId, NotificationType.EventAwardRevoked,
            It.Is<string>(s => s != null &&
                (System.Text.Json.JsonDocument.Parse(s).RootElement.GetProperty("reason").GetString() ?? "").Contains("Bài dự thi vi phạm quy chế")),
            It.IsAny<CancellationToken>()), Times.Once);
        notif.Verify(n => n.CreateAsync(pC.UserId, NotificationType.EventResultPublished,
            It.Is<string>(s => s.Contains("\"eventId\":1")), It.IsAny<CancellationToken>()), Times.Once);

        // Thu hồi không gán người thay ghi một bản đối soát hoàn ngân sách.
        var countBefore = _uow.Items<EventAward>().Count;
        var revokedOnly = await rewardSvc.RevokeAwardAsync(1, aw2.Id, new RevokeAwardRequestDto { Reason = "Thu hồi bỏ trống." });
        Assert.Equal(EventAwardStatus.Revoked, aw2.Status);
        Assert.Equal(aw2.Id, revokedOnly.Id);
        Assert.Equal(countBefore + 1, _uow.Items<EventAward>().Count);
        var returned = Assert.Single(_uow.Items<EventAward>().Where(a => a.ReallocatedFromAwardId == aw2.Id));
        Assert.Equal(EventAwardStatus.UnclaimedBudgetReturned, returned.Status);
        Assert.Null(returned.ParticipationId);

        // Hết hạn khiếu nại và không còn đơn mở -> thu hồi bị từ chối (409).
        ev.ResultsPublishedAt = _now.AddHours(-49);
        await Assert.ThrowsAsync<ConflictException>(() =>
            rewardSvc.RevokeAwardAsync(1, replaced.Id, new RevokeAwardRequestDto { Reason = "Thu hồi quá hạn" }));
    }

    [Fact]
    public async Task RevokeAward_WithOpenAppealAfterWindow_AcceptsAppealInSameStep()
    {
        var (subAct, pA, pB, _) = SeedThreeCreatorSubmissionScores(90, 80, 70);
        var ev = _uow.Items<Event>().Single();
        ev.ResultsFinalizedAt = _now.AddHours(-50);
        ev.ResultsPublishedAt = _now.AddHours(-49);
        var aw1 = _uow.Seed(new EventAward
        {
            Id = 100, EventId = 1, EventActivityId = subAct.Id, ResultSource = EventAwardResultSource.SubmissionScore,
            TargetAudience = EventAudience.Creator, RankOrTier = "Giải Nhất", SlotIndex = 1, RewardName = "Quà Nhất",
            ParticipationId = pA.Id, Status = EventAwardStatus.AppealHold
        });
        var appeal = _uow.Seed(new EventAppeal
        {
            Id = 300, EventId = 1, ParticipationId = pB.Id, AppellantUserId = pB.UserId, ReasonText = "Bài của tôi điểm cao hơn",
            Kind = EventAppealKind.Ranking, TargetAwardId = aw1.Id, Status = EventAppealStatus.UnderReview
        });

        var notif = new Mock<INotificationService>();
        var rewardSvc = new EventRewardService(_uow, _audit.Object, notif.Object);

        // Đơn không thuộc sự kiện / không tồn tại -> lỗi, suất giữ nguyên.
        await Assert.ThrowsAsync<NotFoundException>(() =>
            rewardSvc.RevokeAwardAsync(1, aw1.Id, new RevokeAwardRequestDto { Reason = "Sai đơn", AppealId = 999 }));
        Assert.Equal(EventAwardStatus.AppealHold, aw1.Status);

        var replaced = await rewardSvc.RevokeAwardAsync(1, aw1.Id, new RevokeAwardRequestDto
        {
            Reason = "Chấm lại: bài của B cao hơn.",
            ReplacementParticipationId = pB.Id,
            AppealId = appeal.Id
        });

        Assert.Equal(EventAwardStatus.Revoked, aw1.Status);
        Assert.Equal(pB.Id, replaced.ParticipationId);
        Assert.Equal(EventAppealStatus.Accepted, appeal.Status);
        Assert.Equal(1, appeal.ReviewedByUserId);
        Assert.Equal("Chấm lại: bài của B cao hơn.", appeal.ResolutionNote);
        Assert.NotNull(appeal.ResolvedAt);
        notif.Verify(n => n.CreateAsync(pB.UserId, NotificationType.EventAppealAccepted,
            It.Is<string>(s => s.Contains("\"appealId\":300")), It.IsAny<CancellationToken>()), Times.Once);

        // Đơn đã có kết luận -> không dùng lại được để thu hồi sau hạn.
        await Assert.ThrowsAsync<ConflictException>(() =>
            rewardSvc.RevokeAwardAsync(1, replaced.Id, new RevokeAwardRequestDto { Reason = "Lần nữa", AppealId = appeal.Id }));
    }
}
