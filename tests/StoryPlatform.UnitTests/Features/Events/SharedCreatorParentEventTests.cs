using System.Linq.Expressions;
using Moq;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.Events.DTOs;
using StoryPlatform.Application.Features.Events.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.Events;

public class SharedCreatorParentEventTests
{
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IAuditLogWriter> _audit = new();

    private Mock<IGenericRepository<T>> MockRepo<T>(params T[] items) where T : class
    {
        var repo = new Mock<IGenericRepository<T>>();
        repo.Setup(x => x.FindAsync(It.IsAny<Expression<Func<T, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<T, bool>> predicate, string? _, CancellationToken _) =>
                items.Where(predicate.Compile()).ToArray());
        repo.Setup(x => x.FirstOrDefaultAsync(It.IsAny<Expression<Func<T, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<T, bool>> predicate, string? _, CancellationToken _) =>
                items.FirstOrDefault(predicate.Compile()));
        repo.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(items);
        _uow.Setup(x => x.Repository<T>()).Returns(repo.Object);
        return repo;
    }

    [Fact]
    public async Task SubmitForApproval_SharedCreatorParent_RequiresRoleTasksAndParentAwardSlots()
    {
        var ev = new Event
        {
            Id = 10,
            AssignedModeratorId = 1,
            Status = EventStatus.Draft,
            Format = EventFormat.SharedCreatorParent,
            ScoringMode = EventScoringMode.ParentVoteAverage,
            MinVotesPerSubmission = 5,
            MaxParentRegistrations = 3,
            SubmissionCutoffAt = DateTime.UtcNow.AddDays(2),
            ReviewCutoffAt = DateTime.UtcNow.AddDays(3),
            VotingStartAt = DateTime.UtcNow.AddDays(4),
            VotingEndAt = DateTime.UtcNow.AddDays(10)
        };
        var eventsRepo = MockRepo(ev);
        eventsRepo.Setup(x => x.GetByIdAsync(10, It.IsAny<CancellationToken>())).ReturnsAsync(ev);

        var parentTrack = new EventTrack
        {
            Id = 20,
            EventId = 10,
            TrackType = EventTrackType.Parent,
            AwardAllMissionCompleters = true,
            RegistrationStartAt = DateTime.UtcNow,
            RegistrationEndAt = DateTime.UtcNow.AddDays(9),
            ExecutionStartAt = DateTime.UtcNow.AddDays(4),
            ExecutionEndAt = DateTime.UtcNow.AddDays(10)
        };
        var creatorTrack = new EventTrack
        {
            Id = 21,
            EventId = 10,
            TrackType = EventTrackType.Creator,
            RegistrationStartAt = DateTime.UtcNow,
            RegistrationEndAt = DateTime.UtcNow.AddDays(2),
            ExecutionStartAt = DateTime.UtcNow,
            ExecutionEndAt = DateTime.UtcNow.AddDays(2)
        };
        MockRepo(creatorTrack, parentTrack);

        var rule = new EventRuleVersion { Id = 30, EventId = 10, IsActive = true, VersionNo = 1, RulesText = "Thể lệ" };
        MockRepo(rule);

        // Only Creator task exists, missing Parent task -> 400
        MockRepo(new EventTaskDefinition
        {
            Id = 40,
            EventRuleVersionId = 30,
            TargetTrackType = EventTrackType.Creator,
            VerifierType = TaskVerifierType.SubmitApprovedStory,
            Title = "Nộp truyện",
            TargetValue = 1
        });
        MockRepo(
            new EventAward { Id = 50, EventId = 10, TrackId = 21, RankOrTier = "Giải Nhất", RewardType = EventRewardType.CreatorPackage, RewardName = "Creator test pack", CreditGrantAmount = 5, SubmitCreditGrantAmount = 2 },
            new EventAward { Id = 51, EventId = 10, TrackId = 20, RankOrTier = "Quà Parent", RewardType = EventRewardType.PlatformCredits, CreditGrantAmount = 2 },
            new EventAward { Id = 52, EventId = 10, TrackId = 20, RankOrTier = "Quà Parent", RewardType = EventRewardType.PlatformCredits, CreditGrantAmount = 2 });

        var svc = new EventManagementService(_uow.Object, _audit.Object);
        var exMissingTask = await Assert.ThrowsAsync<BadRequestException>(() => svc.SubmitForApprovalAsync(1, 10));
        Assert.Contains("Parent", exMissingTask.Message);

        // Now add Parent task, but Parent award slots (2) < MaxParentRegistrations (3) -> 400
        MockRepo(
            new EventTaskDefinition { Id = 40, EventRuleVersionId = 30, TargetTrackType = EventTrackType.Creator, VerifierType = TaskVerifierType.SubmitApprovedStory, Title = "Nộp truyện", TargetValue = 1 },
            new EventTaskDefinition { Id = 41, EventRuleVersionId = 30, TargetTrackType = EventTrackType.Parent, VerifierType = TaskVerifierType.VoteEventStories, Title = "Đọc & bình chọn", TargetValue = 3 });

        var exInsufficientSlots = await Assert.ThrowsAsync<BadRequestException>(() => svc.SubmitForApprovalAsync(1, 10));
        Assert.Contains("3", exInsufficientSlots.Message);

        // Add 3rd Parent slot -> succeeds
        MockRepo(
            new EventAward { Id = 50, EventId = 10, TrackId = 21, RankOrTier = "Giải Nhất", RewardType = EventRewardType.CreatorPackage, RewardName = "Creator test pack", CreditGrantAmount = 5, SubmitCreditGrantAmount = 2 },
            new EventAward { Id = 51, EventId = 10, TrackId = 20, RankOrTier = "Quà Parent", RewardType = EventRewardType.PlatformCredits, CreditGrantAmount = 2 },
            new EventAward { Id = 52, EventId = 10, TrackId = 20, RankOrTier = "Quà Parent", RewardType = EventRewardType.PlatformCredits, CreditGrantAmount = 2 },
            new EventAward { Id = 53, EventId = 10, TrackId = 20, RankOrTier = "Quà Parent", RewardType = EventRewardType.PlatformCredits, CreditGrantAmount = 2 });

        MockRepo(new SubscriptionPlan { Id = 80, Name = "Creator test pack", Audience = PlanAudience.Creator,
            PlanType = PlanType.CreatorPack, AiCredits = 5, SubmitCredits = 2 });
        var submitted = await svc.SubmitForApprovalAsync(1, 10);
        Assert.Equal(EventStatus.PendingApproval, submitted.Status);
    }

    [Fact]
    public void RankCreatorByVotes_AppliesThresholdAndTieBreakers()
    {
        var t0 = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        var candidates = new[]
        {
            new CreatorVoteRankingCandidate(SubmissionId: 1, ParticipationId: 11, StoryId: 101, CreatorUserId: 201, StoryTitle: "T1", ValidVoteCount: 2, AverageStars: 4.5m, FirstSubmittedAt: t0.AddHours(2)),
            new CreatorVoteRankingCandidate(SubmissionId: 2, ParticipationId: 12, StoryId: 102, CreatorUserId: 202, StoryTitle: "T2", ValidVoteCount: 3, AverageStars: 4.5m, FirstSubmittedAt: t0.AddHours(1)),
            new CreatorVoteRankingCandidate(SubmissionId: 3, ParticipationId: 13, StoryId: 103, CreatorUserId: 203, StoryTitle: "T3", ValidVoteCount: 1, AverageStars: 5.0m, FirstSubmittedAt: t0)
        };

        var ranked = EventMissionEvaluator.RankCreatorByVotes(candidates, minVotesPerSubmission: 2);
        Assert.Equal(2, ranked.Count);
        Assert.Equal(102, ranked[0].StoryId); // Same 4.5 avg, beats 101 by ValidVoteCount 3 > 2
        Assert.Equal(101, ranked[1].StoryId);
    }

    [Fact]
    public async Task DisqualifyVote_ReducesParentProgressBelowTarget()
    {
        var ev = new Event { Id = 1, Status = EventStatus.Published, Format = EventFormat.SharedCreatorParent, AssignedModeratorId = 5 };
        var eventsRepo = MockRepo(ev);
        eventsRepo.Setup(x => x.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(ev);

        var vote = new EventVote { Id = 77, EventId = 1, StoryId = 101, StoryVersionId = 500, ParticipationId = 88, ParentUserId = 99, Stars = 5 };
        var votesRepo = MockRepo(vote);
        votesRepo.Setup(x => x.GetByIdAsync(77, It.IsAny<CancellationToken>())).ReturnsAsync(vote);

        var participation = new EventParticipation
        {
            Id = 88,
            EventId = 1,
            UserId = 99,
            TrackId = 2,
            AcceptedRuleVersionId = 10,
            Status = EventParticipationStatus.MissionCompleted,
            MissionCompletedAt = DateTime.UtcNow
        };
        var partRepo = MockRepo(participation);
        partRepo.Setup(x => x.GetByIdAsync(88, It.IsAny<CancellationToken>())).ReturnsAsync(participation);

        var track = new EventTrack { Id = 2, EventId = 1, TrackType = EventTrackType.Parent };
        var trackRepo = MockRepo(track);
        trackRepo.Setup(x => x.GetByIdAsync(2, It.IsAny<CancellationToken>())).ReturnsAsync(track);

        var rule = new EventRuleVersion { Id = 10, EventId = 1, IsActive = true, CompletionMode = TrackCompletionMode.AllMandatory };
        var ruleRepo = MockRepo(rule);
        ruleRepo.Setup(x => x.GetByIdAsync(10, It.IsAny<CancellationToken>())).ReturnsAsync(rule);

        MockRepo(new EventTaskDefinition
        {
            Id = 11,
            EventRuleVersionId = 10,
            TargetTrackType = EventTrackType.Parent,
            VerifierType = TaskVerifierType.VoteEventStories,
            TargetValue = 1,
            IsMandatory = true
        });
        MockRepo<EventStory>();
        MockRepo<EventTaskProgress>();
        MockRepo<ChildProfile>();
        MockRepo<Story>();

        var svc = new EventParticipationService(_uow.Object, _audit.Object);
        await svc.DisqualifyVoteAsync(5, 77, new DisqualifyEventVoteRequestDto { Reason = "Gian lận bình chọn" });

        Assert.True(vote.IsDisqualified);
        Assert.Equal("Gian lận bình chọn", vote.DisqualifiedReason);
        Assert.Equal(EventParticipationStatus.Registered, participation.Status);
    }
}
