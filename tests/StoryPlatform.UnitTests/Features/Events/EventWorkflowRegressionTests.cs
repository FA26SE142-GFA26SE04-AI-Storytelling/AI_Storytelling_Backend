using Microsoft.AspNetCore.DataProtection;
using StoryPlatform.Infrastructure.Security;
using Moq;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.Events.DTOs;
using StoryPlatform.Application.Features.Events.Services;
using StoryPlatform.Application.Features.Moderation.DTOs;
using StoryPlatform.Application.Features.Moderation.Interfaces;
using StoryPlatform.Application.Features.TokenQuota.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.UnitTests.Features.TokenQuota;
using Xunit;

namespace StoryPlatform.UnitTests.Features.Events;

public class EventWorkflowRegressionTests
{
    private readonly InMemoryUnitOfWork store = new();
    private readonly IAuditLogWriter audit = Mock.Of<IAuditLogWriter>();
    private readonly DateTime now = DateTime.UtcNow;

    private EventParticipationService ParentFixture(TaskVerifierType type, int target = 1)
    {
        store.Seed(new Event { Id = 1, Status = EventStatus.Published });
        store.Seed(new EventTrack { Id = 2, EventId = 1, TrackType = EventTrackType.Parent,
            ExecutionStartAt = now.AddDays(-3), ExecutionEndAt = now.AddDays(1), WaiveWeeklyLibraryLimit = true });
        store.Seed(new EventParticipation { Id = 3, EventId = 1, TrackId = 2, UserId = 50,
            AcceptedRuleVersionId = 4, CreatedAt = now.AddDays(-2) });
        store.Seed(new EventRuleVersion { Id = 4, EventId = 1, IsActive = true });
        store.Seed(new EventTaskDefinition { Id = 5, EventRuleVersionId = 4, VerifierType = type, TargetValue = target, IsMandatory = true });
        store.Seed(new ChildProfile { Id = 6, OwnerUserId = 50 });
        store.Seed(new EventStory { Id = 7, EventId = 1, StoryId = 8, IsActive = true });
        store.Seed(new Story { Id = 8, IsPublished = true, OutputMode = StoryOutputMode.Learning });
        store.Seed(new StoryVersion { Id = 9, StoryId = 8, IsCurrent = true });
        return new EventParticipationService(store, audit);
    }

    [Theory]
    [InlineData(TaskVerifierType.ReadEventStories)]
    [InlineData(TaskVerifierType.CompleteStoryQuiz)]
    [InlineData(TaskVerifierType.SaveStoryVocabulary)]
    [InlineData(TaskVerifierType.AnswerChildQuestion)]
    [InlineData(TaskVerifierType.RateAndReviewStory)]
    [InlineData(TaskVerifierType.ActiveDaysCount)]
    public async Task AutomaticTaskCannotBeSelfDeclared(TaskVerifierType type)
    {
        var service = ParentFixture(type);
        await Assert.ThrowsAsync<BadRequestException>(() => service.RecordTaskProgressAsync(50, 1, 5,
            new() { StoryId = 8, SelfDeclaredPayloadJson = "{}" }));
        Assert.Empty(store.Items<EventTaskProgress>());
        // Even old injected progress cannot satisfy automatic verification.
        store.Seed(new EventTaskProgress { ParticipationId = 3, TaskDefinitionId = 5, StoryId = 8, CompletedAt = now });
        Assert.False((await service.GetMissionDashboardAsync(50, 1)).AllRequirementsMet);
    }

    [Theory]
    [InlineData(TaskVerifierType.ReadEventStories)]
    [InlineData(TaskVerifierType.CompleteStoryQuiz)]
    [InlineData(TaskVerifierType.SaveStoryVocabulary)]
    [InlineData(TaskVerifierType.AnswerChildQuestion)]
    [InlineData(TaskVerifierType.RateAndReviewStory)]
    [InlineData(TaskVerifierType.ActiveDaysCount)]
    public async Task DashboardUsesRealActivityAndIsStableOnRefresh(TaskVerifierType type)
    {
        var service = ParentFixture(type);
        store.Seed(new ReadingSession { Id = 10, ChildProfileId = 6, StoryId = 8, StoryVersionId = 9,
            Status = SessionStatus.Completed, StartedAt = now.AddMinutes(-10), CompletedAt = now.AddMinutes(-1) });
        store.Seed(new QuizItem { Id = 11, StoryVersionId = 9 });
        store.Seed(new QuizAttempt { ReadingSessionId = 10, QuizItemId = 11, AnswerGiven = "A", AnsweredAt = now.AddMinutes(-2) });
        store.Seed(new StoryVocabulary { Id = 12, StoryVersionId = 9 });
        store.Seed(new VocabularyNotebookEntry { ChildProfileId = 6, StoryVocabularyId = 12, CollectedAt = now.AddMinutes(-2) });
        store.Seed(new StorySummary { Id = 13, ReadingSessionId = 10, ChildProfileId = 6, StoryId = 8 });
        store.Seed(new StorySummaryFlag { StorySummaryId = 13, AnsweredAt = now.AddMinutes(-2) });
        store.Seed(new StoryRating { ReviewerUserId = 50, StoryId = 8, CreatedAt = now.AddSeconds(-30) });
        Assert.True((await service.GetMissionDashboardAsync(50, 1)).AllRequirementsMet);
        Assert.Equal(1, (await service.GetMissionDashboardAsync(50, 1)).Tasks.Single().CurrentValue);
        Assert.Empty(store.Items<EventTaskProgress>());
    }

    [Fact]
    public async Task ReadTasksUseTheirOwnStoryAndDurationFiltersAndRatingsRequireReading()
    {
        var service = ParentFixture(TaskVerifierType.ReadEventStories);
        store.Items<EventTaskDefinition>().Single().ConfigJson = """{"storyIds":[8],"minReadSeconds":60}""";
        store.Seed(new EventStory { EventId = 1, StoryId = 18, IsActive = true });
        store.Seed(new StoryCategory { StoryId = 18, ContentCategoryId = 42 });
        store.Seed(new EventTaskDefinition { Id = 21, EventRuleVersionId = 4, VerifierType = TaskVerifierType.ReadEventStories,
            TargetValue = 1, IsMandatory = true, ConfigJson = """{"categoryIds":[42]}""" });
        store.Seed(new EventTaskDefinition { Id = 22, EventRuleVersionId = 4, VerifierType = TaskVerifierType.RateAndReviewStory,
            TargetValue = 1, IsMandatory = true, ConfigJson = """{"storyIds":[8]}""" });
        store.Seed(new StoryRating { ReviewerUserId = 50, StoryId = 8, Status = StoryRatingStatus.Published, CreatedAt = now.AddSeconds(-10) });
        store.Seed(new ReadingSession { Id = 30, ChildProfileId = 6, StoryId = 18, Status = SessionStatus.Completed,
            StartedAt = now.AddMinutes(-5), CompletedAt = now.AddMinutes(-1), TimeSpentSeconds = 70 });
        var first = (await service.GetMissionDashboardAsync(50, 1)).Tasks.ToDictionary(t => t.TaskDefinitionId);
        Assert.False(first[5].IsCompleted);
        Assert.True(first[21].IsCompleted);
        Assert.False(first[22].IsCompleted);

        var reading = new ReadingSession { Id = 31, ChildProfileId = 6, StoryId = 8, Status = SessionStatus.Completed,
            StartedAt = now.AddMinutes(-4), CompletedAt = now.AddMinutes(-1), TimeSpentSeconds = 59 };
        store.Seed(reading);
        Assert.False((await service.GetMissionDashboardAsync(50, 1)).Tasks.Single(t => t.TaskDefinitionId == 5).IsCompleted);
        reading.TimeSpentSeconds = 60;
        Assert.True((await service.GetMissionDashboardAsync(50, 1)).AllRequirementsMet);
    }

    [Fact]
    public async Task ManualCheckinsCountDistinctSelectedStories()
    {
        var service = ParentFixture(TaskVerifierType.RealLifePracticeCheckin, 2);
        store.Seed(new EventStory { EventId = 1, StoryId = 18, IsActive = true });
        store.Seed(new Story { Id = 18, IsPublished = true });
        foreach (var storyId in new[] { 8, 8, 18 })
            await service.RecordTaskProgressAsync(50, 1, 5, new() { StoryId = storyId, SelfDeclaredPayloadJson = "{}" });
        Assert.Equal(2, (await service.GetMissionDashboardAsync(50, 1)).Tasks.Single().CurrentValue);
    }

    [Fact]
    public async Task PartialQuizAndOtherFamilyActivityDoNotCompleteTasks()
    {
        var service = ParentFixture(TaskVerifierType.CompleteStoryQuiz);
        store.Seed(new ReadingSession { Id = 10, StoryId = 8, StoryVersionId = 9, ChildProfileId = 6, StartedAt = now });
        store.Seed(new QuizItem { Id = 11, StoryVersionId = 9 });
        store.Seed(new QuizItem { Id = 12, StoryVersionId = 9 });
        store.Seed(new QuizAttempt { ReadingSessionId = 10, QuizItemId = 11, AnswerGiven = "A", AnsweredAt = now });
        store.Seed(new ReadingSession { Id = 20, StoryId = 8, StoryVersionId = 9, ChildProfileId = 99, StartedAt = now });
        foreach (var id in new[] { 11, 12 }) store.Seed(new QuizAttempt { ReadingSessionId = 20, QuizItemId = id, AnswerGiven = "A", AnsweredAt = now });
        Assert.False((await service.GetMissionDashboardAsync(50, 1)).AllRequirementsMet);
        store.Seed(new QuizAttempt { ReadingSessionId = 10, QuizItemId = 12, AnswerGiven = "B", AnsweredAt = now });
        Assert.True((await service.GetMissionDashboardAsync(50, 1)).AllRequirementsMet);
    }

    [Fact]
    public async Task CreatorAwardsRequireRubricScoresAndUseThePublishedTieBreakers()
    {
        store.Seed(new Event { Id = 1, AssignedModeratorId = 50, Status = EventStatus.Published });
        store.Seed(new EventTrack { Id = 2, EventId = 1, TrackType = EventTrackType.Creator, ExecutionEndAt = now.AddDays(-1) });
        store.Seed(new EventRuleVersion { Id = 4, EventId = 1, IsActive = true });
        for (var i = 0; i < 4; i++)
        {
            store.Seed(new EventParticipation { Id = 10 + i, EventId = 1, TrackId = 2, AcceptedRuleVersionId = 4,
                Status = EventParticipationStatus.Verified });
            store.Seed(new EventSubmission { Id = 20 + i, EventId = 1, ParticipationId = 10 + i,
                Status = EventSubmissionStatus.Approved, LockedStoryVersionId = 30 + i, SubmittedAt = now.AddDays(-5 + i) });
            store.Seed(new EventAward { Id = 40 + i, EventId = 1, TrackId = 2, SlotIndex = i, Status = EventAwardStatus.AwardProposed });
        }
        foreach (var submissionId in new[] { 21, 21, 22 })
            store.Seed(new AuditLog { EntityId = submissionId, EntityType = nameof(EventSubmission), Action = "EventStorySubmitted" });
        var rewards = new EventRewardService(store, audit);
        await Assert.ThrowsAsync<BadRequestException>(() => rewards.EvaluateSubmissionAsync(50, 23,
            new() { CriteriaScoresJson = """{"total":100}""" }));
        await rewards.EvaluateSubmissionAsync(50, 20,
            new() { TotalWeightedScore = 100, CriteriaScoresJson = """{"creativity":80,"education":80,"structure":80,"media":80}""" });
        foreach (var submissionId in new[] { 21, 22 })
            await rewards.EvaluateSubmissionAsync(50, submissionId,
                new() { CriteriaScoresJson = """{"creativity":100,"education":60,"structure":80,"media":80}""" });
        Assert.All(store.Items<EventEvaluation>(), evaluation => Assert.Equal(80m, evaluation.TotalWeightedScore));
        await rewards.ProposeAwardsAsync(50, 1);
        Assert.Equal(new int?[] { 12, 11, 10, null }, store.Items<EventAward>().OrderBy(a => a.Id).Select(a => a.ParticipationId));
    }

    [Fact]
    public async Task MissionCompletionAwardsUseCompletionOrderWithoutRubricOrVotes()
    {
        store.Seed(new Event { Id = 1, AssignedModeratorId = 50, Status = EventStatus.Published, ScoringMode = EventScoringMode.MissionCompletion });
        store.Seed(new EventTrack { Id = 2, EventId = 1, TrackType = EventTrackType.Creator, ExecutionEndAt = now.AddDays(-1) });
        store.Seed(new EventParticipation { Id = 10, EventId = 1, TrackId = 2, Status = EventParticipationStatus.Verified,
            MissionCompletedAt = now.AddDays(-2) });
        store.Seed(new EventParticipation { Id = 11, EventId = 1, TrackId = 2, Status = EventParticipationStatus.Verified,
            MissionCompletedAt = now.AddDays(-3) });
        var award = store.Seed(new EventAward { Id = 30, EventId = 1, TrackId = 2 });
        store.Seed(new EventSubmission { Id = 20, EventId = 1, ParticipationId = 11,
            Status = EventSubmissionStatus.Approved, LockedStoryVersionId = 31 });

        var rewards = new EventRewardService(store, audit);
        await rewards.ProposeAwardsAsync(50, 1);
        Assert.Equal(11, award.ParticipationId);
        await Assert.ThrowsAsync<ConflictException>(() => rewards.EvaluateSubmissionAsync(50, 20, new()));
        var participation = new EventParticipationService(store, audit);
        await Assert.ThrowsAsync<ConflictException>(() => participation.CastVoteAsync(50, 1, new() { StoryId = 1, Stars = 5 }));
    }

    [Fact]
    public async Task CrossRoleVotingRanksAndAssignsAwardsSeparatelyForEachAuthorGroup()
    {
        store.Seed(new Event
        {
            Id = 1, AssignedModeratorId = 50, Status = EventStatus.Published, ScoringMode = EventScoringMode.CrossRoleVoteAverage,
            MinVotesPerSubmission = 1, VotingEndAt = now.AddHours(-1)
        });
        store.Seed(new EventTrack { Id = 2, EventId = 1, TrackType = EventTrackType.Creator, ExecutionEndAt = now.AddHours(-1) });
        store.Seed(new EventTrack { Id = 3, EventId = 1, TrackType = EventTrackType.Parent, ExecutionEndAt = now.AddHours(-1) });
        store.Seed(new EventParticipation { Id = 10, EventId = 1, TrackId = 2, Status = EventParticipationStatus.Verified });
        store.Seed(new EventParticipation { Id = 11, EventId = 1, TrackId = 3, Status = EventParticipationStatus.Verified });
        store.Seed(new EventSubmission { Id = 20, EventId = 1, ParticipationId = 10, StoryId = 30,
            Status = EventSubmissionStatus.Approved, LockedStoryVersionId = 40 });
        store.Seed(new EventSubmission { Id = 21, EventId = 1, ParticipationId = 11, StoryId = 31,
            Status = EventSubmissionStatus.Approved, LockedStoryVersionId = 41, ParentSubmissionText = "Bài Parent" });
        store.Seed(new EventVote { Id = 50, EventId = 1, ParticipationId = 11, EventSubmissionId = 20,
            StoryId = 30, StoryVersionId = 40, Stars = 5 });
        store.Seed(new EventVote { Id = 51, EventId = 1, ParticipationId = 10, EventSubmissionId = 21,
            StoryId = 31, StoryVersionId = 41, Stars = 3 });
        var creatorAward = store.Seed(new EventAward { Id = 60, EventId = 1, TrackId = 2 });
        var parentAward = store.Seed(new EventAward { Id = 61, EventId = 1, TrackId = 3 });

        var entries = (await new EventParticipationService(store, audit).GetLeaderboardAsync(1, includeHidden: true)).Entries;
        Assert.Equal(2, entries.Count);
        Assert.All(entries, entry => Assert.Equal(1, entry.Rank));
        await new EventRewardService(store, audit).ProposeAwardsAsync(50, 1);
        Assert.Equal(10, creatorAward.ParticipationId);
        Assert.Equal(11, parentAward.ParticipationId);
    }

    [Fact]
    public async Task ReadingHookSkipsDisqualifiedParticipation()
    {
        ParentFixture(TaskVerifierType.ReadEventStories);
        store.Items<EventParticipation>().Single().Status = EventParticipationStatus.Disqualified;

        await new EventIntegrationHooks(store).RecordActivityForActiveEventsAsync(
            50, 6, 8, TaskVerifierType.ReadEventStories, readingSessionId: 10);

        Assert.Empty(store.Items<EventTaskProgress>());
    }

    [Fact]
    public async Task ExemptionEndsWithEventWindowOrDisqualification()
    {
        ParentFixture(TaskVerifierType.ReadEventStories);
        var hooks = new EventIntegrationHooks(store);
        Assert.True(await hooks.IsStoryExemptFromWeeklyLimitAsync(50, 8));
        store.Items<Event>().Single().Status = EventStatus.Closed;
        Assert.False(await hooks.IsStoryExemptFromWeeklyLimitAsync(50, 8));
        store.Items<Event>().Single().Status = EventStatus.Published;
        store.Items<EventParticipation>().Single().Status = EventParticipationStatus.Disqualified;
        Assert.False(await hooks.IsStoryExemptFromWeeklyLimitAsync(50, 8));
        store.Items<EventParticipation>().Single().Status = EventParticipationStatus.Registered;
        store.Items<EventTrack>().Single().ExecutionEndAt = now.AddDays(-1);
        Assert.False(await hooks.IsStoryExemptFromWeeklyLimitAsync(50, 8));
    }

    [Fact]
    public async Task AwardApprovalReassignsAStaleProposalAndFreezesTheMatchingResults()
    {
        store.Seed(new Event
        {
            Id = 1, AssignedModeratorId = 50, Status = EventStatus.Published, ScoringMode = EventScoringMode.ParentVoteAverage,
            MinVotesPerSubmission = 1, VotingEndAt = now.AddMinutes(-1)
        });
        store.Seed(new EventTrack { Id = 2, EventId = 1, TrackType = EventTrackType.Creator, ExecutionEndAt = now.AddDays(-1) });
        store.Seed(new EventParticipation { Id = 3, EventId = 1, TrackId = 2, Status = EventParticipationStatus.Verified });
        store.Seed(new EventParticipation { Id = 4, EventId = 1, TrackId = 2, Status = EventParticipationStatus.Verified });
        store.Seed(new EventParticipation { Id = 5, EventId = 1, Status = EventParticipationStatus.VerificationFailed });
        store.Seed(new EventSubmission
        {
            Id = 6, EventId = 1, ParticipationId = 3, StoryId = 11, LockedStoryVersionId = 21,
            Status = EventSubmissionStatus.Approved, CreatedAt = now.AddDays(-4), SubmittedAt = now.AddDays(-2)
        });
        store.Seed(new EventSubmission
        {
            Id = 7, EventId = 1, ParticipationId = 4, StoryId = 12, LockedStoryVersionId = 22,
            Status = EventSubmissionStatus.Approved, CreatedAt = now.AddDays(-3), SubmittedAt = now.AddDays(-3)
        });
        store.Seed(new EventVote { Id = 8, EventId = 1, ParticipationId = 5, EventSubmissionId = 6, StoryId = 11, StoryVersionId = 21, Stars = 5 });
        store.Seed(new EventVote { Id = 9, EventId = 1, ParticipationId = 5, EventSubmissionId = 7, StoryId = 12, StoryVersionId = 22, Stars = 5 });
        store.Seed(new EventAward { Id = 10, EventId = 1, TrackId = 2 });
        var rewards = new EventRewardService(store, audit);
        var participation = new EventParticipationService(store, audit);

        Assert.Equal(3, Assert.Single(await rewards.ProposeAwardsAsync(50, 1)).ParticipationId);
        await participation.DisqualifyVoteAsync(50, 8, new() { Reason = "Test invalid vote" });
        Assert.Equal(4, Assert.Single(await rewards.ApproveAwardsAsync(51, 1)).ParticipationId);

        var finalized = await participation.GetLeaderboardAsync(1, includeHidden: false);
        Assert.True(finalized.ResultsFinalized);
        Assert.Equal(4, Assert.Single(finalized.Entries.Where(x => x.MeetsThreshold)).ParticipationId);
        await Assert.ThrowsAsync<ConflictException>(() => participation.DisqualifyVoteAsync(50, 9, new() { Reason = "After finalization" }));
    }

    [Fact]
    public async Task ExpiredCreatorPackUsesFrozenVoteRankingAndKeepsBothGrants()
    {
        var finalResults = new EventLeaderboardDto
        {
            Entries =
            [
                new() { Rank = 1, ParticipationId = 3, MeetsThreshold = true },
                new() { Rank = 2, ParticipationId = 4, MeetsThreshold = true }
            ]
        };
        store.Seed(new Event
        {
            Id = 1, AssignedModeratorId = 6, ScoringMode = EventScoringMode.ParentVoteAverage, ResultsFinalizedAt = now.AddDays(-2),
            FinalLeaderboardSnapshotJson = System.Text.Json.JsonSerializer.Serialize(finalResults)
        });
        store.Seed(new EventTrack { Id = 2, EventId = 1, TrackType = EventTrackType.Creator, MaxReallocationRounds = 1 });
        store.Seed(new EventParticipation { Id = 3, EventId = 1, TrackId = 2, Status = EventParticipationStatus.Verified });
        store.Seed(new EventParticipation { Id = 4, EventId = 1, TrackId = 2, Status = EventParticipationStatus.Verified });
        store.Seed(new EventAward
        {
            Id = 5, EventId = 1, TrackId = 2, ParticipationId = 3, Status = EventAwardStatus.AwaitingClaim,
            ClaimDeadlineAt = now.AddDays(-1), RewardType = EventRewardType.CreatorPackage,
            CreditGrantAmount = 6, SubmitCreditGrantAmount = 20
        });

        Assert.Equal(1, await new EventRewardService(store, audit).ProcessExpiredClaimsAsync(6, 1));

        var replacement = Assert.Single(store.Items<EventAward>().Where(a => a.ReallocatedFromAwardId == 5));
        Assert.Equal(4, replacement.ParticipationId);
        Assert.Equal(6, replacement.CreditGrantAmount);
        Assert.Equal(20, replacement.SubmitCreditGrantAmount);
    }

    [Fact]
    public async Task PrizeClaimLocksTheEventBeforeGrantingAndCannotGrantTwice()
    {
        store.Seed(new EventParticipation { Id = 3, UserId = 50 });
        store.Seed(new EventAward { Id = 4, EventId = 1, ParticipationId = 3,
            Status = EventAwardStatus.AwaitingClaim, RewardType = EventRewardType.PlatformCredits, CreditGrantAmount = 2 });
        var rewards = new EventRewardService(store, audit);
        await rewards.ClaimRewardAsync(50, 4, new());
        await Assert.ThrowsAsync<ConflictException>(() => rewards.ClaimRewardAsync(50, 4, new()));
        var lot = Assert.Single(store.Items<CreditLot>());
        Assert.Equal(CreditSource.EventReward, lot.Source);
        Assert.Equal(2, lot.Remaining);
        Assert.Equal(new[] { "begin", "event-lock:1", "commit", "begin", "event-lock:1", "rollback" }, store.Calls);
        store.Seed(new UserAccount { Id = 50, Role = UserRole.ContentCreator, Status = AccountStatus.Active });
        store.Seed(new Event { Id = 1, Status = EventStatus.Closed });
        var story = store.Seed(new Story { Id = 7, Scope = StoryScope.Creator, AuthorUserId = 50 });
        var quota = new TokenQuotaService(store);
        await quota.GetCreatorQuotaAsync(50);
        foreach (var monthly in store.Items<CreditLot>().Where(x => x.Source == CreditSource.CreatorMonthly)) monthly.Remaining = 0;
        await quota.IncrementCreatorUsageAsync(50, story);
        Assert.Equal(1, lot.Remaining);
    }

    [Fact]
    public async Task EventSubmissionUsesExistingPublicReviewService()
    {
        store.Seed(new Event { Id = 1, MainCategoryId = 20, Status = EventStatus.Published, CreatedAt = now.AddDays(-3) });
        store.Seed(new EventTrack { Id = 2, EventId = 1, TrackType = EventTrackType.Creator,
            ExecutionStartAt = now.AddDays(-1), ExecutionEndAt = now.AddDays(1) });
        store.Seed(new EventParticipation { Id = 3, EventId = 1, TrackId = 2, UserId = 50 });
        var story = store.Seed(new Story { Id = 8, AuthorUserId = 50, Scope = StoryScope.Creator, CreatedAt = now.AddDays(-2) });
        var version = store.Seed(new StoryVersion { Id = 9, StoryId = 8, IsCurrent = true });
        var submission = new Mock<IPublicStorySubmissionService>();
        submission.Setup(s => s.SubmitAsync(50, It.Is<SubmitPublicStoryRequestDto>(r => r.StoryId == 8 && r.StoryVersionId == 9 && r.ContentCategoryIds.Contains(20)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PublicStorySubmissionResultDto());
        var service = new EventParticipationService(store, audit, publicSubmission: submission.Object);
        await service.SubmitCreatorStoryAsync(50, 1, new() { StoryId = 8 });
        submission.VerifyAll();
        story.IsPublished = true;
        version.Moderation.ReviewStatus = VersionReviewStatus.Approved;
        var result = await service.SubmitCreatorStoryAsync(50, 1, new() { StoryId = 8 });
        Assert.Equal(EventSubmissionStatus.Approved, result.Status);
        Assert.Equal(9, result.LockedStoryVersionId);
        Assert.Single(store.Items<EventStory>());
    }

    [Fact]
    public async Task RetryFulfillment_CopiesRecipientInfoFromPreviousAttempt()
    {
        store.Seed(new Event { Id = 1, Status = EventStatus.Published, AssignedModeratorId = 9 });
        store.Seed(new EventAward { Id = 4, EventId = 1, ParticipationId = 3, Status = EventAwardStatus.Failed });
        store.Seed(new RewardFulfillment { Id = 10, EventAwardId = 4, AttemptNo = 1, RecipientInfoProtectedJson = "{\"x\":1}", Status = RewardFulfillmentStatus.Failed });

        await new EventRewardService(store, audit).UpdateFulfillmentAsync(9, 4, new FulfillRewardRequestDto
        {
            Delivered = true,
            TrackingOrBankTxCode = "TX-999"
        });

        var retry = Assert.Single(store.Items<RewardFulfillment>().Where(f => f.AttemptNo == 2));
        Assert.Equal("{\"x\":1}", retry.RecipientInfoProtectedJson);
        Assert.Equal(RewardFulfillmentStatus.Delivered, retry.Status);
    }

    [Fact]
    public async Task Claim_PhysicalGiftWithoutAddress_ThrowsBadRequest()
    {
        store.Seed(new EventParticipation { Id = 3, EventId = 1, UserId = 50 });
        var award = store.Seed(new EventAward { Id = 4, EventId = 1, ParticipationId = 3, Status = EventAwardStatus.AwaitingClaim, RewardType = EventRewardType.PhysicalGift });

        var ex = await Assert.ThrowsAsync<BadRequestException>(() => new EventRewardService(store, audit).ClaimRewardAsync(50, 4, new ClaimRewardRequestDto
        {
            RecipientName = "Nguyen Van A",
            PhoneNumber = "0909123456",
            ShippingAddress = "   "
        }));

        Assert.Equal("Cần nhập họ tên, số điện thoại và địa chỉ nhận quà.", ex.Message);
        Assert.Equal(EventAwardStatus.AwaitingClaim, award.Status);
    }

    [Fact]
    public async Task Claim_CashWithoutBankFields_RejectsLegacyAddress()
    {
        store.Seed(new EventParticipation { Id = 3, EventId = 1, UserId = 50 });
        var award = store.Seed(new EventAward { Id = 4, EventId = 1, ParticipationId = 3, Status = EventAwardStatus.AwaitingClaim, RewardType = EventRewardType.CashTransfer });

        var ex = await Assert.ThrowsAsync<BadRequestException>(() => new EventRewardService(store, audit).ClaimRewardAsync(50, 4, new ClaimRewardRequestDto
        {
            RecipientName = "Nguyen Van A",
            ShippingAddress = "Bank details in the wrong field"
        }));

        Assert.Equal("Cần nhập ngân hàng, số tài khoản và tên chủ tài khoản.", ex.Message);
        Assert.Equal(EventAwardStatus.AwaitingClaim, award.Status);
    }

    [Fact]
    public async Task Claim_CashWithPayoutAccount_EmptyFields_Succeeds()
    {
        store.Seed(new EventParticipation { Id = 3, EventId = 1, UserId = 50 });
        store.Seed(new CreatorPayoutAccount { Id = 7, CreatorUserId = 50, BankName = "VCB", AccountNumber = "123456", AccountHolderName = "Creator A" });
        var award = store.Seed(new EventAward { Id = 4, EventId = 1, ParticipationId = 3, Status = EventAwardStatus.AwaitingClaim, RewardType = EventRewardType.CashTransfer });

        var dto = await new EventRewardService(store, audit, recipientProtector: new RewardRecipientProtector(new EphemeralDataProtectionProvider())).ClaimRewardAsync(50, 4, new ClaimRewardRequestDto());

        Assert.Equal(EventAwardStatus.FulfillmentProcessing, dto.Status);
        var fulfillment = Assert.Single(store.Items<RewardFulfillment>());
        Assert.DoesNotContain("123456", fulfillment.RecipientInfoProtectedJson);
    }

    [Fact]
    public async Task Claim_CashWithBankFields_ProtectsPayloadAndRestrictsReading()
    {
        store.Seed(new Event { Id = 1, AssignedModeratorId = 9 });
        store.Seed(new EventModeratorAssignment { EventId = 1, ModeratorId = 11 });
        store.Seed(new UserAccount { Id = 11, Role = UserRole.Moderator, Status = AccountStatus.Active });
        store.Seed(new UserAccount { Id = 12, Role = UserRole.Moderator, Status = AccountStatus.Active });
        store.Seed(new EventParticipation { Id = 3, EventId = 1, UserId = 50 });
        store.Seed(new EventAward { Id = 4, EventId = 1, ParticipationId = 3, Status = EventAwardStatus.AwaitingClaim, RewardType = EventRewardType.CashTransfer });

        var protector = new RewardRecipientProtector(new EphemeralDataProtectionProvider());
        var rewards = new EventRewardService(store, audit, recipientProtector: protector);
        var dto = await rewards.ClaimRewardAsync(50, 4, new ClaimRewardRequestDto
        {
            BankName = "Test Bank",
            BankAccountNumber = "000QA12345",
            BankAccountHolder = "QA Parent"
        });

        Assert.Equal(EventAwardStatus.FulfillmentProcessing, dto.Status);
        var fulfillment = Assert.Single(store.Items<RewardFulfillment>());
        Assert.DoesNotContain("000QA12345", fulfillment.RecipientInfoProtectedJson);
        await Assert.ThrowsAsync<ForbiddenException>(() => rewards.GetRewardRecipientAsync(12, 4));

        var recipient = await rewards.GetRewardRecipientAsync(11, 4);
        Assert.Equal("000QA12345", recipient.BankAccountNumber);
        Assert.False(recipient.IsLegacyPlaintext);
        Mock.Get(audit).Verify(x => x.LogAsync(11, "EventRewardRecipientViewed", nameof(EventAward), 4,
            null, It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ApproveLegacyMissionCompletion_AssignsVerifiedParentsWithoutProposal()
    {
        store.Seed(new Event { Id = 1, Status = EventStatus.Published, ScoringMode = EventScoringMode.MissionCompletion });
        store.Seed(new EventTrack { Id = 2, EventId = 1, TrackType = EventTrackType.Parent, ExecutionEndAt = now.AddHours(-1) });
        store.Seed(new EventParticipation { Id = 3, EventId = 1, TrackId = 2, UserId = 50, Status = EventParticipationStatus.Verified });
        store.Seed(new EventAward { Id = 4, EventId = 1, TrackId = 2, Status = EventAwardStatus.AwardProposed });

        var award = Assert.Single(await new EventRewardService(store, audit).ApproveAwardsAsync(9, 1));
        Assert.Equal(3, award.ParticipationId);
        Assert.Equal(EventAwardStatus.AwaitingClaim, award.Status);
    }

    [Fact]
    public async Task ApproveLegacyRubric_RejectsApprovedSubmissionWithoutEvaluation()
    {
        var campaign = store.Seed(new Event { Id = 1, Status = EventStatus.Published, ScoringMode = EventScoringMode.Rubric });
        store.Seed(new EventTrack { Id = 2, EventId = 1, TrackType = EventTrackType.Creator, ExecutionEndAt = now.AddHours(-1) });
        store.Seed(new EventParticipation { Id = 3, EventId = 1, TrackId = 2, UserId = 50, Status = EventParticipationStatus.Verified });
        store.Seed(new EventAward { Id = 4, EventId = 1, TrackId = 2, Status = EventAwardStatus.AwardProposed });
        store.Seed(new EventSubmission { Id = 5, EventId = 1, ParticipationId = 3, Status = EventSubmissionStatus.Approved, LockedStoryVersionId = 100 });

        var error = await Assert.ThrowsAsync<ConflictException>(() => new EventRewardService(store, audit).ApproveAwardsAsync(9, 1));
        Assert.Contains("chưa được chấm điểm", error.Message);
        Assert.Null(campaign.ResultsFinalizedAt);
    }

    [Theory]
    [InlineData(1, false, false)]
    [InlineData(49, false, true)]
    [InlineData(1, true, true)]
    public async Task ApproveAwards_RespectsAppealWindow(int hoursSinceVerification, bool resolved, bool canApprove)
    {
        var campaign = store.Seed(new Event { Id = 1, Status = EventStatus.Published, ScoringMode = EventScoringMode.MissionCompletion });
        store.Seed(new EventTrack { Id = 2, EventId = 1, TrackType = EventTrackType.Parent, ExecutionEndAt = now.AddDays(-3) });
        store.Seed(new EventParticipation { Id = 3, EventId = 1, UserId = 50, TrackId = 2,
            Status = EventParticipationStatus.VerificationFailed, VerifiedAt = now.AddHours(-hoursSinceVerification) });
        if (resolved)
            store.Seed(new EventAppeal { EventId = 1, ParticipationId = 3, Status = EventAppealStatus.Rejected });

        var rewards = new EventRewardService(store, audit);
        if (canApprove)
        {
            Assert.Empty(await rewards.ApproveAwardsAsync(9, 1));
            Assert.NotNull(campaign.ResultsFinalizedAt);
        }
        else
        {
            var error = await Assert.ThrowsAsync<ConflictException>(() => rewards.ApproveAwardsAsync(9, 1));
            Assert.Contains("hạn khiếu nại", error.Message);
            Assert.Null(campaign.ResultsFinalizedAt);
        }
    }

    [Fact]
    public async Task CreateAppeal_AfterFortyEightHours_RejectsRequest()
    {
        store.Seed(new Event { Id = 1, Status = EventStatus.Published });
        store.Seed(new EventParticipation { Id = 3, EventId = 1, UserId = 50,
            Status = EventParticipationStatus.VerificationFailed, VerifiedAt = now.AddHours(-49) });

        var error = await Assert.ThrowsAsync<ConflictException>(() =>
            new EventParticipationService(store, audit).CreateAppealAsync(50, 1,
                new CreateAppealRequestDto { ParticipationId = 3, ReasonText = "Kiểm tra lại kết quả." }));
        Assert.Contains("48 giờ", error.Message);
    }

    [Fact]
    public async Task GetRewardRecipient_ReadsLegacyBankDetailsAndFlagsPlaintext()
    {
        store.Seed(new Event { Id = 1, AssignedModeratorId = 9 });
        store.Seed(new EventAward { Id = 4, EventId = 1, RewardType = EventRewardType.CashTransfer });
        store.Seed(new RewardFulfillment { EventAwardId = 4, AttemptNo = 1,
            RecipientInfoProtectedJson = """{"BankAccountText":"QA Bank 000123","PhoneNumber":"000"}"""
        });

        var recipient = await new EventRewardService(store, audit).GetRewardRecipientAsync(9, 4);
        Assert.True(recipient.IsLegacyPlaintext);
        Assert.Equal("QA Bank 000123", recipient.LegacyBankAccountText);
        Assert.Null(recipient.ShippingAddress);
    }

    [Fact]
    public async Task RecordActivity_SameReadingSessionTwice_RecordsOnce()
    {
        var now = DateTime.UtcNow;
        store.Seed(new Event { Id = 1, Status = EventStatus.Published, ConfigVersion = EventConfigVersion.ActivityBased });
        store.Seed(new EventActivity
        {
            Id = 10, EventId = 1, ActivityType = EventActivityType.ReadingMissions,
            StartAt = now.AddHours(-1), EndAt = now.AddHours(1)
        });
        store.Seed(new EventRuleVersion { Id = 20, EventId = 1, IsActive = true });
        store.Seed(new EventTaskDefinition
        {
            Id = 30, EventRuleVersionId = 20, VerifierType = TaskVerifierType.ReadEventStories,
            TargetValue = 1, Title = "Đọc truyện"
        });
        store.Seed(new EventParticipation
        {
            Id = 40, EventId = 1, EventActivityId = 10, UserId = 50,
            AcceptedRuleVersionId = 20
        });
        store.Seed(new EventStory { Id = 60, EventId = 1, StoryId = 70, IsActive = true });

        var hooks = new EventIntegrationHooks(store);
        await hooks.RecordActivityForActiveEventsAsync(50, 51, 70, TaskVerifierType.ReadEventStories, readingSessionId: 900);
        await hooks.RecordActivityForActiveEventsAsync(50, 51, 70, TaskVerifierType.ReadEventStories, readingSessionId: 900);

        Assert.Single(store.Items<EventTaskProgress>());
    }

    [Fact]
    public async Task AcquireLocks_NewEventAppearsAfterStoryLock_ThrowsConflictWithoutLockingEventAfterStory()
    {
        const int storyId = 100;
        const int versionId = 101;
        store.Seed(new UserAccount { Id = 11, Username = "mod11", Role = UserRole.Moderator, Status = AccountStatus.Active });
        store.Seed(new Story { Id = storyId, Title = "Truyện 100" });
        store.Seed(new StoryVersion { Id = versionId, StoryId = storyId, VersionNo = 1, IsCurrent = true });
        store.Seed(new ModerationItem { Id = 1, ItemType = ModerationItemType.PublicStoryReview, Status = ModerationItemStatus.Pending, StoryId = storyId, StoryVersionId = versionId });
        store.Seed(new Event { Id = 5, Title = "Sự kiện 5", Status = EventStatus.Published, ConfigVersion = EventConfigVersion.ActivityBased, AssignedModeratorId = 11 });
        store.Seed(new Event { Id = 7, Title = "Sự kiện 7", Status = EventStatus.Published, ConfigVersion = EventConfigVersion.ActivityBased, AssignedModeratorId = 11 });
        store.Seed(new EventActivity { Id = 5, EventId = 5, ActivityType = EventActivityType.Submission, ReviewCutoffAt = now.AddHours(2) });
        store.Seed(new EventActivity { Id = 7, EventId = 7, ActivityType = EventActivityType.Submission, ReviewCutoffAt = now.AddHours(2) });
        store.Seed(new EventSubmission { Id = 50, EventId = 5, StoryId = storyId, CurrentStoryVersionId = versionId, Status = EventSubmissionStatus.PendingReview });

        var lockKeys = new List<long>();
        var uow = new LockRecordingUnitOfWork(store, key =>
        {
            lockKeys.Add(key);
            if (key == storyId)
            {
                store.Seed(new EventSubmission { Id = 70, EventId = 7, StoryId = storyId, CurrentStoryVersionId = versionId, Status = EventSubmissionStatus.PendingReview });
            }
        });

        var queue = new StoryPlatform.Application.Features.Moderation.Services.ModerationQueueService(uow, audit);
        await Assert.ThrowsAsync<ConflictException>(() => queue.ClaimAsync(11, 1));
        Assert.Equal(
            new long[] { StoryPlatform.Infrastructure.Persistence.Repositories.UnitOfWork.EventLockKey(5), storyId },
            lockKeys);
    }

    [Fact]
    public async Task AcquireLocks_StableLinks_LocksEventsThenStory()
    {
        const int storyId = 100;
        const int versionId = 101;
        store.Seed(new UserAccount { Id = 11, Username = "mod11", Role = UserRole.Moderator, Status = AccountStatus.Active });
        store.Seed(new Story { Id = storyId, Title = "Truyện 100" });
        store.Seed(new StoryVersion { Id = versionId, StoryId = storyId, VersionNo = 1, IsCurrent = true });
        store.Seed(new ModerationItem { Id = 1, ItemType = ModerationItemType.PublicStoryReview, Status = ModerationItemStatus.Pending, StoryId = storyId, StoryVersionId = versionId });
        store.Seed(new Event { Id = 5, Title = "Sự kiện 5", Status = EventStatus.Published, ConfigVersion = EventConfigVersion.ActivityBased, AssignedModeratorId = 11 });
        store.Seed(new Event { Id = 3, Title = "Sự kiện 3", Status = EventStatus.Published, ConfigVersion = EventConfigVersion.ActivityBased, AssignedModeratorId = 11 });
        store.Seed(new EventActivity { Id = 5, EventId = 5, ActivityType = EventActivityType.Submission, ReviewCutoffAt = now.AddHours(2) });
        store.Seed(new EventActivity { Id = 3, EventId = 3, ActivityType = EventActivityType.Submission, ReviewCutoffAt = now.AddHours(2) });
        store.Seed(new EventSubmission { Id = 50, EventId = 5, StoryId = storyId, CurrentStoryVersionId = versionId, Status = EventSubmissionStatus.PendingReview });
        store.Seed(new EventSubmission { Id = 30, EventId = 3, StoryId = storyId, CurrentStoryVersionId = versionId, Status = EventSubmissionStatus.PendingReview });

        var lockKeys = new List<long>();
        var uow = new LockRecordingUnitOfWork(store, key => lockKeys.Add(key));

        var queue = new StoryPlatform.Application.Features.Moderation.Services.ModerationQueueService(uow, audit);
        var result = await queue.ClaimAsync(11, 1);

        Assert.Equal(ModerationItemStatus.Claimed.ToString(), result.Status);
        Assert.Equal(
            new long[]
            {
                StoryPlatform.Infrastructure.Persistence.Repositories.UnitOfWork.EventLockKey(3),
                StoryPlatform.Infrastructure.Persistence.Repositories.UnitOfWork.EventLockKey(5),
                storyId
            },
            lockKeys);
    }

    [Fact]
    public async Task LoadActiveByVersion_DuplicatePending_KeepsNewestSubmissionId()
    {
        const int storyId = 100;
        const int versionId = 101;
        store.Seed(new UserAccount { Id = 11, Username = "mod11", Role = UserRole.Moderator, Status = AccountStatus.Active });
        store.Seed(new Story { Id = storyId, Title = "Truyện 100" });
        store.Seed(new StoryVersion { Id = versionId, StoryId = storyId, VersionNo = 1, IsCurrent = true });
        store.Seed(new ModerationItem { Id = 1, ItemType = ModerationItemType.PublicStoryReview, Status = ModerationItemStatus.Pending, StoryId = storyId, StoryVersionId = versionId });
        store.Seed(new Event { Id = 5, Title = "Sự kiện 5", Status = EventStatus.Published, ConfigVersion = EventConfigVersion.ActivityBased, AssignedModeratorId = 11 });
        store.Seed(new EventActivity { Id = 5, EventId = 5, ActivityType = EventActivityType.Submission, ReviewCutoffAt = now.AddHours(2) });
        store.Seed(new EventSubmission { Id = 10, EventId = 5, StoryId = storyId, CurrentStoryVersionId = versionId, Status = EventSubmissionStatus.PendingReview });
        store.Seed(new EventSubmission { Id = 20, EventId = 5, StoryId = storyId, CurrentStoryVersionId = versionId, Status = EventSubmissionStatus.PendingReview });

        var queue = new StoryPlatform.Application.Features.Moderation.Services.ModerationQueueService(store, audit);
        var result = await queue.ListAsync(11, new ModerationQueueQueryDto { OnlyMyEventSubmissions = true });

        var item = Assert.Single(result.Items);
        var sub = Assert.Single(item.EventSubmissions);
        Assert.Equal(20, sub.EventSubmissionId);
    }

    [Fact]
    public async Task EventLocks_DoNotUseNegativeStoryNamespaceKeys()
    {
        store.Seed(new UserAccount { Id = 50, Username = "u50", Status = AccountStatus.Active });
        store.Seed(new Event { Id = 7, Title = "Sự kiện 7", Status = EventStatus.Published });
        var service = new EventParticipationService(store, audit);

        await Assert.ThrowsAnyAsync<Exception>(() =>
            service.RegisterAsync(50, 7, new RegisterEventRequestDto { AcceptRules = true }));

        Assert.DoesNotContain("lock:-7", store.Calls);
        Assert.Equal(1, store.Calls.Count(c => c == "event-lock:7"));
    }

    [Fact]
    public void UnitOfWork_EventLockKey_DiffersFromStoryKeyForNegativeId()
    {
        Assert.NotEqual(
            StoryPlatform.Infrastructure.Persistence.Repositories.UnitOfWork.StoryLockKey(-7),
            StoryPlatform.Infrastructure.Persistence.Repositories.UnitOfWork.EventLockKey(7));

        const int largeEventId = 1_500_000_000;
        var largeKey = StoryPlatform.Infrastructure.Persistence.Repositories.UnitOfWork.EventLockKey(largeEventId);
        Assert.Equal(0x45564E5400000000L, largeKey & unchecked((long)0xFFFFFFFF00000000UL));
        Assert.NotEqual(
            StoryPlatform.Infrastructure.Persistence.Repositories.UnitOfWork.StoryLockKey(largeEventId),
            largeKey);
    }

    [Fact]
    public async Task RecordActivity_AcquiresEventLockBeforeCheckingExisting()
    {
        store.Seed(new Event { Id = 1, Status = EventStatus.Published, ConfigVersion = EventConfigVersion.ActivityBased });
        store.Seed(new EventActivity
        {
            Id = 2,
            EventId = 1,
            ActivityType = EventActivityType.ReadingMissions,
            StartAt = now.AddDays(-1),
            EndAt = now.AddDays(1),
        });
        store.Seed(new EventRuleVersion { Id = 4, EventId = 1, IsActive = true });
        store.Seed(new EventTaskDefinition { Id = 5, EventRuleVersionId = 4, VerifierType = TaskVerifierType.ReadEventStories, TargetValue = 1 });
        store.Seed(new EventParticipation
        {
            Id = 3,
            EventId = 1,
            EventActivityId = 2,
            UserId = 50,
            AcceptedRuleVersionId = 4,
            Status = EventParticipationStatus.Registered,
        });
        store.Seed(new EventStory { Id = 7, EventId = 1, StoryId = 8, IsActive = true });

        var ops = new List<string>();
        var uow = new ProgressTracingUnitOfWork(store, ops);
        var hooks = new EventIntegrationHooks(uow);

        await hooks.RecordActivityForActiveEventsAsync(50, 6, 8, TaskVerifierType.ReadEventStories, readingSessionId: 900);

        var lockIdx = ops.IndexOf("event-lock:1");
        var checkIdx = ops.IndexOf("check-progress");
        Assert.True(lockIdx >= 0, "Expected AcquireEventLockAsync(1) to be called.");
        Assert.True(checkIdx > lockIdx, "Expected EventTaskProgress check to occur after AcquireEventLockAsync(1).");
    }

    [Fact]
    public async Task RecordActivity_OwnsTransaction_CommitsOnce()
    {
        store.Seed(new Event { Id = 1, Status = EventStatus.Published, ConfigVersion = EventConfigVersion.ActivityBased });
        store.Seed(new EventActivity
        {
            Id = 2,
            EventId = 1,
            ActivityType = EventActivityType.ReadingMissions,
            StartAt = now.AddDays(-1),
            EndAt = now.AddDays(1),
        });
        store.Seed(new EventRuleVersion { Id = 4, EventId = 1, IsActive = true });
        store.Seed(new EventTaskDefinition { Id = 5, EventRuleVersionId = 4, VerifierType = TaskVerifierType.ReadEventStories, TargetValue = 1 });
        store.Seed(new EventParticipation
        {
            Id = 3,
            EventId = 1,
            EventActivityId = 2,
            UserId = 50,
            AcceptedRuleVersionId = 4,
            Status = EventParticipationStatus.Registered,
        });
        store.Seed(new EventStory { Id = 7, EventId = 1, StoryId = 8, IsActive = true });

        var hooks = new EventIntegrationHooks(store);
        await hooks.RecordActivityForActiveEventsAsync(50, 6, 8, TaskVerifierType.ReadEventStories, readingSessionId: 901);

        Assert.Equal(1, store.Calls.Count(c => c == "begin"));
        Assert.Equal(1, store.Calls.Count(c => c == "commit"));
    }

    [Fact]
    public async Task RecordActivity_SameSessionTwice_RecordsOnce()
    {
        store.Seed(new Event { Id = 1, Status = EventStatus.Published, ConfigVersion = EventConfigVersion.ActivityBased });
        store.Seed(new EventActivity
        {
            Id = 2,
            EventId = 1,
            ActivityType = EventActivityType.ReadingMissions,
            StartAt = now.AddDays(-1),
            EndAt = now.AddDays(1),
        });
        store.Seed(new EventRuleVersion { Id = 4, EventId = 1, IsActive = true });
        store.Seed(new EventTaskDefinition { Id = 5, EventRuleVersionId = 4, VerifierType = TaskVerifierType.ReadEventStories, TargetValue = 1 });
        store.Seed(new EventParticipation
        {
            Id = 3,
            EventId = 1,
            EventActivityId = 2,
            UserId = 50,
            AcceptedRuleVersionId = 4,
            Status = EventParticipationStatus.Registered,
        });
        store.Seed(new EventStory { Id = 7, EventId = 1, StoryId = 8, IsActive = true });

        var hooks = new EventIntegrationHooks(store);
        await hooks.RecordActivityForActiveEventsAsync(50, 6, 8, TaskVerifierType.ReadEventStories, readingSessionId: 902);
        await hooks.RecordActivityForActiveEventsAsync(50, 6, 8, TaskVerifierType.ReadEventStories, readingSessionId: 902);

        Assert.Single(store.Items<EventTaskProgress>());
    }

    [Fact]
    public async Task LoadActiveLinks_IncludeExpired_DuplicatePending_ReturnsOneLinkWithNewestSubmission()
    {
        const int storyId = 200;
        const int versionId = 201;
        store.Seed(new UserAccount { Id = 11, Username = "mod11", Role = UserRole.Moderator, Status = AccountStatus.Active });
        store.Seed(new UserAccount { Id = 20, Username = "creator20", Role = UserRole.ContentCreator, Status = AccountStatus.Active });
        store.Seed(new Story { Id = storyId, Title = "Truyện 200", AuthorUserId = 20, Scope = StoryScope.Creator, Status = StoryStatus.Approved });
        store.Seed(new StoryVersion
        {
            Id = versionId,
            StoryId = storyId,
            VersionNo = 1,
            IsCurrent = true,
            Title = "Truyện 200",
        });
        store.Seed(new ModerationItem
        {
            Id = 55,
            ItemType = ModerationItemType.PublicStoryReview,
            Status = ModerationItemStatus.Claimed,
            ClaimedByUserId = 11,
            ClaimedAt = now.AddMinutes(-5),
            StoryId = storyId,
            StoryVersionId = versionId,
            CreatedByUserId = 20,
            RowVersion = 1,
        });
        store.Seed(new Event
        {
            Id = 5,
            Title = "Sự kiện 5",
            Status = EventStatus.Published,
            ConfigVersion = EventConfigVersion.ActivityBased,
            AssignedModeratorId = 11,
        });
        store.Seed(new EventActivity
        {
            Id = 5,
            EventId = 5,
            ActivityType = EventActivityType.Submission,
            ReviewCutoffAt = now.AddHours(-1),
        });
        var oldSub = store.Seed(new EventSubmission
        {
            Id = 10,
            EventId = 5,
            StoryId = storyId,
            CurrentStoryVersionId = versionId,
            Status = EventSubmissionStatus.PendingReview,
        });
        var newSub = store.Seed(new EventSubmission
        {
            Id = 20,
            EventId = 5,
            StoryId = storyId,
            CurrentStoryVersionId = versionId,
            Status = EventSubmissionStatus.PendingReview,
        });

        var review = new StoryPlatform.Application.Features.Moderation.Services.PublicStoryReviewService(
            store,
            audit,
            Mock.Of<StoryPlatform.Application.Features.Notifications.Interfaces.INotificationService>());

        await review.RejectAsync(11, 55, new RejectRequestDto { Reason = "Không đạt tiêu chuẩn." });

        Assert.Equal(EventSubmissionStatus.ReviewExpired, newSub.Status);
        Assert.Equal(EventSubmissionStatus.PendingReview, oldSub.Status);
    }

    [Fact]
    public async Task EventManagement_ReturnsMainCategoryNameInListAndDetail()
    {
        store.Seed(new ContentCategory { Id = 12, Code = "adventure", DisplayName = "Phiêu lưu", IsActive = true });
        store.Seed(new Event
        {
            Id = 301,
            Title = "Hành trình kỳ thú",
            Status = EventStatus.Published,
            MainCategoryId = 12,
        });

        var svc = new EventManagementService(store, audit);
        var list = await svc.ListEventsAsync(includeDrafts: false);
        var summary = Assert.Single(list);
        Assert.Equal("Phiêu lưu", summary.MainCategoryName);

        var detail = await svc.GetEventDetailAsync(301);
        Assert.Equal("Phiêu lưu", detail.MainCategoryName);
    }

    private sealed class ProgressTracingUnitOfWork(
        StoryPlatform.Application.Abstractions.Persistence.IUnitOfWork inner,
        List<string> ops) : StoryPlatform.Application.Abstractions.Persistence.IUnitOfWork
    {
        public StoryPlatform.Application.Abstractions.Persistence.IGenericRepository<T> Repository<T>() where T : class
        {
            var repo = inner.Repository<T>();
            return typeof(T) == typeof(EventTaskProgress)
                ? (StoryPlatform.Application.Abstractions.Persistence.IGenericRepository<T>)(object)new ProgressTracingRepo((StoryPlatform.Application.Abstractions.Persistence.IGenericRepository<EventTaskProgress>)repo, ops)
                : repo;
        }
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => inner.SaveChangesAsync(cancellationToken);
        public bool HasActiveTransaction => inner.HasActiveTransaction;
        public Task BeginTransactionAsync(CancellationToken cancellationToken = default) { ops.Add("begin"); return inner.BeginTransactionAsync(cancellationToken); }
        public Task AcquireTransactionLockAsync(int resourceId, CancellationToken cancellationToken = default) => inner.AcquireTransactionLockAsync(resourceId, cancellationToken);
        public Task AcquireEventLockAsync(int eventId, CancellationToken cancellationToken = default) { ops.Add($"event-lock:{eventId}"); return inner.AcquireEventLockAsync(eventId, cancellationToken); }
        public Task CommitTransactionAsync(CancellationToken cancellationToken = default) { ops.Add("commit"); return inner.CommitTransactionAsync(cancellationToken); }
        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) { ops.Add("rollback"); return inner.RollbackTransactionAsync(cancellationToken); }
        public void DiscardTrackedChanges() => inner.DiscardTrackedChanges();
        public void Dispose() => inner.Dispose();
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    private sealed class ProgressTracingRepo(
        StoryPlatform.Application.Abstractions.Persistence.IGenericRepository<EventTaskProgress> inner,
        List<string> ops) : StoryPlatform.Application.Abstractions.Persistence.IGenericRepository<EventTaskProgress>
    {
        public Task<EventTaskProgress?> GetByIdAsync(object id, CancellationToken cancellationToken = default) => inner.GetByIdAsync(id, cancellationToken);
        public Task<IReadOnlyList<EventTaskProgress>> GetAllAsync(CancellationToken cancellationToken = default) => inner.GetAllAsync(cancellationToken);
        public Task<IReadOnlyList<EventTaskProgress>> FindAsync(System.Linq.Expressions.Expression<Func<EventTaskProgress, bool>> predicate, string? includeProperties = null, CancellationToken cancellationToken = default) => inner.FindAsync(predicate, includeProperties, cancellationToken);
        public Task<EventTaskProgress?> FirstOrDefaultAsync(System.Linq.Expressions.Expression<Func<EventTaskProgress, bool>> predicate, string? includeProperties = null, CancellationToken cancellationToken = default) => inner.FirstOrDefaultAsync(predicate, includeProperties, cancellationToken);
        public Task<(IReadOnlyList<EventTaskProgress> Items, int TotalCount)> GetPagedAsync(int pageIndex, int pageSize, System.Linq.Expressions.Expression<Func<EventTaskProgress, bool>>? filter = null, Func<IQueryable<EventTaskProgress>, IOrderedQueryable<EventTaskProgress>>? orderBy = null, string? includeProperties = null, CancellationToken cancellationToken = default) => inner.GetPagedAsync(pageIndex, pageSize, filter, orderBy, includeProperties, cancellationToken);
        public Task<int> CountAsync(System.Linq.Expressions.Expression<Func<EventTaskProgress, bool>>? predicate = null, CancellationToken cancellationToken = default) => inner.CountAsync(predicate, cancellationToken);
        public Task<bool> ExistsAsync(System.Linq.Expressions.Expression<Func<EventTaskProgress, bool>> predicate, CancellationToken cancellationToken = default)
        {
            ops.Add("check-progress");
            return inner.ExistsAsync(predicate, cancellationToken);
        }
        public IQueryable<EventTaskProgress> Query() => inner.Query();
        public Task<EventTaskProgress> AddAsync(EventTaskProgress entity, CancellationToken cancellationToken = default) => inner.AddAsync(entity, cancellationToken);
        public Task AddRangeAsync(IEnumerable<EventTaskProgress> entities, CancellationToken cancellationToken = default) => inner.AddRangeAsync(entities, cancellationToken);
        public void Update(EventTaskProgress entity) => inner.Update(entity);
        public void Delete(EventTaskProgress entity) => inner.Delete(entity);
        public void DeleteRange(IEnumerable<EventTaskProgress> entities) => inner.DeleteRange(entities);
    }

    private sealed class LockRecordingUnitOfWork(
        StoryPlatform.Application.Abstractions.Persistence.IUnitOfWork inner,
        Action<long> onLock) : StoryPlatform.Application.Abstractions.Persistence.IUnitOfWork
    {
        public StoryPlatform.Application.Abstractions.Persistence.IGenericRepository<T> Repository<T>() where T : class => inner.Repository<T>();
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => inner.SaveChangesAsync(cancellationToken);
        public Task BeginTransactionAsync(CancellationToken cancellationToken = default) => inner.BeginTransactionAsync(cancellationToken);
        public Task CommitTransactionAsync(CancellationToken cancellationToken = default) => inner.CommitTransactionAsync(cancellationToken);
        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => inner.RollbackTransactionAsync(cancellationToken);
        public Task AcquireTransactionLockAsync(int key, CancellationToken cancellationToken = default)
        {
            onLock(key);
            return Task.CompletedTask;
        }
        public Task AcquireTransactionLockAsync(long key, CancellationToken cancellationToken = default)
        {
            onLock(key);
            return Task.CompletedTask;
        }
        public Task AcquireEventLockAsync(int eventId, CancellationToken cancellationToken = default)
        {
            onLock(StoryPlatform.Infrastructure.Persistence.Repositories.UnitOfWork.EventLockKey(eventId));
            return Task.CompletedTask;
        }
        public void DiscardTrackedChanges() => inner.DiscardTrackedChanges();
        public void Dispose() => inner.Dispose();
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
