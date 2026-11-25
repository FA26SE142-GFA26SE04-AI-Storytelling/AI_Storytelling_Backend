using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using StoryPlatform.Application.Features.Events.DTOs;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.Events.Services;

public partial class EventRewardService
{
    private sealed record ActivityAwardCandidate(int ParticipationId, int UserId);
    private sealed record ActivityAwardRanking(int? ActivityId, string Tier, ActivityAwardCandidate[] Candidates);

    private static (int? ActivityId, EventAwardResultSource? Source) RankingSourceKey(EventAward award) =>
        (award.EventActivityId, award.ResultSource);

    private static bool IsUserMatchingAudience(UserRole role, EventAudience? target) => target switch
    {
        null or EventAudience.Both => role is UserRole.User or UserRole.ContentCreator,
        EventAudience.Creator => role == UserRole.ContentCreator,
        EventAudience.Parent => role == UserRole.User,
        _ => false
    };

    private async Task AssignActivityProposedAwardsAsync(
        Event ev,
        IReadOnlyList<EventActivity> activities,
        EventLeaderboardDto? leaderboard,
        CancellationToken ct,
        List<ActivityAwardRanking>? rankings = null)
    {
        if (ev.ResultsFinalizedAt.HasValue) return;
        var eventId = ev.Id;
        var verifiedParts = await _uow.Repository<EventParticipation>().FindAsync(
            p => p.EventId == eventId && p.Status == EventParticipationStatus.Verified, cancellationToken: ct);
        var proposedAwards = await _uow.Repository<EventAward>().FindAsync(
            a => a.EventId == eventId && a.Status == EventAwardStatus.AwardProposed, cancellationToken: ct);
        if (proposedAwards.Count == 0) return;

        var activeFinalized = await _uow.Repository<EventAward>().FindAsync(
            a => a.EventId == eventId && a.Status != EventAwardStatus.AwardProposed &&
                 a.Status != EventAwardStatus.Expired && a.Status != EventAwardStatus.UnclaimedBudgetReturned &&
                 a.Status != EventAwardStatus.Failed && a.ParticipationId != null, cancellationToken: ct);
        var partById = verifiedParts.ToDictionary(p => p.Id);
        var userIds = verifiedParts.Select(p => p.UserId).Distinct().ToList();
        var userRoles = userIds.Count == 0
            ? new Dictionary<int, UserRole>()
            : (await _uow.Repository<UserAccount>().FindAsync(u => userIds.Contains(u.Id), cancellationToken: ct))
                .ToDictionary(u => u.Id, u => u.Role);

        foreach (var sourceGroup in proposedAwards.GroupBy(RankingSourceKey))
        {
            var sourceSample = sourceGroup.First();
            var usedUsers = activeFinalized
                .Where(a => RankingSourceKey(a) == sourceGroup.Key &&
                            a.ParticipationId.HasValue && partById.ContainsKey(a.ParticipationId.Value))
                .Select(a => partById[a.ParticipationId!.Value].UserId)
                .ToHashSet();

            var candidatesByAudience = new Dictionary<EventAudience, List<(int ParticipationId, int UserId)>>();

            var tiersInOrder = sourceGroup
                .GroupBy(a => a.RankOrTier.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => (Tier: g.Key, Slots: g.OrderBy(a => a.SlotIndex).ThenBy(a => a.Id).ToList(), MinId: g.Min(a => a.Id)))
                .OrderBy(t => t.MinId)
                .ToList();

            foreach (var tier in tiersInOrder)
            {
                var audience = tier.Slots[0].TargetAudience ?? EventAudience.Both;
                if (!candidatesByAudience.TryGetValue(audience, out var orderedCandidates))
                {
                    orderedCandidates = await OrderActivityTierRecipientsAsync(
                        ev, sourceSample.ResultSource, audience, activities, verifiedParts, userRoles, leaderboard, ct);
                    candidatesByAudience.Add(audience, orderedCandidates);
                }
                rankings?.Add(new ActivityAwardRanking(sourceSample.EventActivityId, tier.Tier,
                    orderedCandidates.Select(c => new ActivityAwardCandidate(c.ParticipationId, c.UserId)).ToArray()));
                var filtered = orderedCandidates.Where(c => !usedUsers.Contains(c.UserId)).DistinctBy(c => c.UserId).ToList();

                for (var i = 0; i < tier.Slots.Count; i++)
                {
                    if (i < filtered.Count)
                    {
                        tier.Slots[i].ParticipationId = filtered[i].ParticipationId;
                        usedUsers.Add(filtered[i].UserId);
                    }
                    else
                    {
                        tier.Slots[i].ParticipationId = null;
                    }
                    tier.Slots[i].UpdatedAt = DateTime.UtcNow;
                    _uow.Repository<EventAward>().Update(tier.Slots[i]);
                }
            }
        }
    }

    private async Task<List<(int ParticipationId, int UserId)>> OrderActivityTierRecipientsAsync(
        Event ev,
        EventAwardResultSource? resultSource,
        EventAudience? targetAudience,
        IReadOnlyList<EventActivity> activities,
        IReadOnlyList<EventParticipation> verifiedParts,
        Dictionary<int, UserRole> userRoles,
        EventLeaderboardDto? leaderboard,
        CancellationToken ct)
    {
        if (!resultSource.HasValue) return [];

        bool AudienceOk(EventParticipation p) =>
            userRoles.TryGetValue(p.UserId, out var role) && IsUserMatchingAudience(role, targetAudience);

        if (resultSource == EventAwardResultSource.SubmissionScore)
        {
            var subAct = activities.FirstOrDefault(a => a.ActivityType == EventActivityType.Submission);
            if (subAct == null) return [];
            var eligibleParts = verifiedParts.Where(p => p.EventActivityId == subAct.Id && AudienceOk(p)).ToList();
            if (eligibleParts.Count == 0) return [];

            var partById = eligibleParts.ToDictionary(p => p.Id);
            var partIds = partById.Keys.ToHashSet();
            var submissions = await _uow.Repository<EventSubmission>().FindAsync(
                s => s.EventId == ev.Id && s.Status == EventSubmissionStatus.Approved &&
                     s.LockedStoryVersionId != null && partIds.Contains(s.ParticipationId), cancellationToken: ct);

            var candidates = new List<(CreatorRankingCandidate Rank, int UserId)>();
            foreach (var sub in submissions)
            {
                var p = partById[sub.ParticipationId];
                var evals = await _uow.Repository<EventEvaluation>().FindAsync(e => e.EventSubmissionId == sub.Id, cancellationToken: ct);
                if (evals.Count == 0) continue;
                var rubric = await GetRubricAsync(p, ct);
                var scores = evals.Select(e => rubric.Score(e.CriteriaScoresJson)).OfType<(decimal Total, decimal Primary)>().ToList();
                if (scores.Count == 0) continue;
                var submittedCount = await _uow.Repository<AuditLog>().CountAsync(
                    x => x.EntityType == nameof(EventSubmission) && x.EntityId == sub.Id &&
                         (x.Action == "EventStorySubmitted" || x.Action == "EventActivityStorySubmitted"), ct);
                candidates.Add((new CreatorRankingCandidate(sub.Id, p.Id, scores.Average(x => x.Total),
                    scores.Average(x => x.Primary), Math.Max(0, submittedCount - 1), sub.SubmittedAt ?? sub.CreatedAt), p.UserId));
            }

            var userBySub = candidates.ToDictionary(x => x.Rank.SubmissionId, x => x.UserId);
            var rankedSubs = EventMissionEvaluator.RankCreatorSubmissions(candidates.Select(x => x.Rank));
            var seenUsers = new HashSet<int>();
            var result = new List<(int ParticipationId, int UserId)>();
            foreach (var r in rankedSubs)
            {
                var uid = userBySub[r.SubmissionId];
                if (seenUsers.Add(uid))
                    result.Add((r.ParticipationId, uid));
            }
            return result;
        }

        if (resultSource == EventAwardResultSource.VotingAverage)
        {
            if (leaderboard == null) return [];
            var subAct = activities.FirstOrDefault(a => a.ActivityType == EventActivityType.Submission);
            if (subAct == null) return [];
            var eligibleParts = verifiedParts.Where(p => p.EventActivityId == subAct.Id && AudienceOk(p))
                .ToDictionary(p => p.Id);
            var seenUsers = new HashSet<int>();
            var result = new List<(int ParticipationId, int UserId)>();
            foreach (var entry in leaderboard.Entries.Where(e => e.MeetsThreshold && e.Rank > 0).OrderBy(e => e.Rank))
            {
                if (!eligibleParts.TryGetValue(entry.ParticipationId, out var p)) continue;
                if (seenUsers.Add(p.UserId))
                    result.Add((p.Id, p.UserId));
            }
            return result;
        }

        if (resultSource == EventAwardResultSource.MissionCompletion)
        {
            var readAct = activities.FirstOrDefault(a => a.ActivityType == EventActivityType.ReadingMissions);
            if (readAct == null) return [];
            return verifiedParts
                .Where(p => p.EventActivityId == readAct.Id && AudienceOk(p))
                .OrderBy(p => p.MissionCompletedAt ?? p.VerifiedAt ?? p.CreatedAt)
                .ThenBy(p => p.Id)
                .Select(p => (p.Id, p.UserId))
                .ToList();
        }

        return [];
    }

    private async Task<int> ProcessActivityExpiredClaimsLockedAsync(
        int actorUserId,
        Event ev,
        IReadOnlyList<EventActivity> activities,
        IReadOnlyList<EventAward> expiredAwards,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var actById = activities.ToDictionary(a => a.Id);
        var frozenRankings = ev.FinalAwardRankingSnapshotJson is { } json
            ? JsonSerializer.Deserialize<ActivityAwardRanking[]>(json)
            : null;
        var hasVoting = activities.Any(a => a.ActivityType == EventActivityType.Voting);
        var leaderboard = hasVoting
            ? await new EventParticipationService(_uow, _audit).BuildLeaderboardAsync(ev.Id, includeHidden: true, verifiedOnly: true, ct)
            : null;
        var allEventAwards = await _uow.Repository<EventAward>().FindAsync(a => a.EventId == ev.Id, cancellationToken: ct);
        var allParticipations = await _uow.Repository<EventParticipation>().FindAsync(p => p.EventId == ev.Id, cancellationToken: ct);
        var verifiedParts = allParticipations.Where(p => p.Status == EventParticipationStatus.Verified).ToList();
        var partById = verifiedParts.ToDictionary(p => p.Id);
        var userByParticipation = allParticipations.ToDictionary(p => p.Id, p => p.UserId);
        var userIds = verifiedParts.Select(p => p.UserId).Distinct().ToList();
        var userRoles = userIds.Count == 0
            ? new Dictionary<int, UserRole>()
            : (await _uow.Repository<UserAccount>().FindAsync(u => userIds.Contains(u.Id), cancellationToken: ct))
                .ToDictionary(u => u.Id, u => u.Role);

        var claimedOrAttemptedAwards = allEventAwards.Where(a => a.ParticipationId.HasValue).ToList();

        var reallocated = 0;
        foreach (var oldAward in expiredAwards)
        {
            oldAward.Status = EventAwardStatus.Expired;
            oldAward.UpdatedAt = now;
            _uow.Repository<EventAward>().Update(oldAward);

            var maxRounds = oldAward.EventActivityId.HasValue && actById.TryGetValue(oldAward.EventActivityId.Value, out var linkedAct)
                ? linkedAct.MaxReallocationRounds : 1;

            (int ParticipationId, int UserId)? runnerUp = null;
            if (oldAward.ReallocationRound < maxRounds)
            {
                var sourceKey = RankingSourceKey(oldAward);
                var usedUsers = claimedOrAttemptedAwards
                    .Where(a => RankingSourceKey(a) == sourceKey &&
                                a.ParticipationId.HasValue &&
                                userByParticipation.ContainsKey(a.ParticipationId.Value))
                    .Select(a => userByParticipation[a.ParticipationId!.Value])
                    .ToHashSet();

                var ranked = frozenRankings == null
                    ? await OrderActivityTierRecipientsAsync(
                        ev, oldAward.ResultSource, oldAward.TargetAudience, activities, verifiedParts, userRoles, leaderboard, ct)
                    : (frozenRankings.FirstOrDefault(r => r.ActivityId == oldAward.EventActivityId &&
                        string.Equals(r.Tier, oldAward.RankOrTier.Trim(), StringComparison.OrdinalIgnoreCase))?.Candidates ?? [])
                        .Select(c => (c.ParticipationId, c.UserId)).ToList();
                var next = ranked.FirstOrDefault(c => partById.ContainsKey(c.ParticipationId) && !usedUsers.Contains(c.UserId));
                if (next.ParticipationId > 0) runnerUp = next;
            }

            if (runnerUp is { } winner)
            {
                var newAward = new EventAward
                {
                    EventId = oldAward.EventId,
                    EventActivityId = oldAward.EventActivityId,
                    ResultSource = oldAward.ResultSource,
                    TargetAudience = oldAward.TargetAudience,
                    RankOrTier = oldAward.RankOrTier,
                    SlotIndex = oldAward.SlotIndex,
                    RewardType = oldAward.RewardType,
                    RewardName = oldAward.RewardName,
                    UnitValueVnd = oldAward.UnitValueVnd,
                    CreditGrantAmount = oldAward.CreditGrantAmount,
                    SubmitCreditGrantAmount = oldAward.SubmitCreditGrantAmount,
                    ParticipationId = winner.ParticipationId,
                    ReallocatedFromAwardId = oldAward.Id,
                    ReallocationRound = oldAward.ReallocationRound + 1,
                    Status = EventAwardStatus.AwardProposed
                };
                await _uow.Repository<EventAward>().AddAsync(newAward, ct);
                claimedOrAttemptedAwards.Add(newAward);
                reallocated++;
            }
            else
            {
                await _uow.Repository<EventAward>().AddAsync(new EventAward
                {
                    EventId = oldAward.EventId,
                    EventActivityId = oldAward.EventActivityId,
                    ResultSource = oldAward.ResultSource,
                    TargetAudience = oldAward.TargetAudience,
                    RankOrTier = oldAward.RankOrTier,
                    SlotIndex = oldAward.SlotIndex,
                    RewardType = oldAward.RewardType,
                    RewardName = oldAward.RewardName,
                    UnitValueVnd = oldAward.UnitValueVnd,
                    CreditGrantAmount = oldAward.CreditGrantAmount,
                    SubmitCreditGrantAmount = oldAward.SubmitCreditGrantAmount,
                    ReallocatedFromAwardId = oldAward.Id,
                    ReallocationRound = oldAward.ReallocationRound,
                    Status = EventAwardStatus.UnclaimedBudgetReturned
                }, ct);
            }
        }

        await _uow.SaveChangesAsync(ct);
        await _audit.LogAsync(actorUserId, "EventExpiredClaimsProcessed", nameof(Event), ev.Id, null, new { ExpiredCount = expiredAwards.Count, Reallocated = reallocated }, ct);
        return reallocated;
    }
}
