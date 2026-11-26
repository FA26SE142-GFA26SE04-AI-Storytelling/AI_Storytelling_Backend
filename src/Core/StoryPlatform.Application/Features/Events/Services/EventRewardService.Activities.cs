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

    internal async Task RecalculateActivityProposedAwardsInTransactionAsync(
        Event ev, IReadOnlyList<EventActivity> activities, CancellationToken ct)
    {
        var hasVoting = activities.Any(a => a.ActivityType == EventActivityType.Voting);
        var lb = hasVoting
            ? await new EventParticipationService(_uow, _audit).BuildLeaderboardAsync(ev.Id, includeHidden: true, verifiedOnly: true, ct)
            : null;
        await AssignActivityProposedAwardsAsync(ev, activities, lb, ct);
    }

    private async Task AssignActivityProposedAwardsAsync(
        Event ev,
        IReadOnlyList<EventActivity> activities,
        EventLeaderboardDto? leaderboard,
        CancellationToken ct,
        List<ActivityAwardRanking>? rankings = null)
    {
        if (ev.ResultsFinalizedAt.HasValue) return;
        var eventId = ev.Id;
        var allParts = await _uow.Repository<EventParticipation>().FindAsync(
            p => p.EventId == eventId, cancellationToken: ct);
        var verifiedParts = allParts.Where(p => p.Status == EventParticipationStatus.Verified).ToList();
        var proposedAwards = await _uow.Repository<EventAward>().FindAsync(
            a => a.EventId == eventId && a.Status == EventAwardStatus.AwardProposed, cancellationToken: ct);
        if (proposedAwards.Count == 0) return;

        var activeFinalized = await _uow.Repository<EventAward>().FindAsync(
            a => a.EventId == eventId && a.Status != EventAwardStatus.AwardProposed &&
                 a.Status != EventAwardStatus.Expired && a.Status != EventAwardStatus.UnclaimedBudgetReturned &&
                 a.Status != EventAwardStatus.Failed && a.Status != EventAwardStatus.Revoked && a.ParticipationId != null, cancellationToken: ct);
        var partById = allParts.ToDictionary(p => p.Id);
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

            // Suất chủ động bỏ trống vẫn được giữ. Chỉ đưa lựa chọn mất điều kiện trở lại danh sách tự động.
            foreach (var manual in sourceGroup.Where(a => a.ManuallyAssignedAt.HasValue && a.ParticipationId.HasValue).OrderBy(a => a.Id))
            {
                var stillEligible = partById.TryGetValue(manual.ParticipationId!.Value, out var part) &&
                    part.Status == EventParticipationStatus.Verified &&
                    part.EventActivityId.HasValue && part.EventActivityId == ResolveSourceActivity(manual, activities)?.Id &&
                    userRoles.TryGetValue(part.UserId, out var role) && IsUserMatchingAudience(role, manual.TargetAudience);
                if (stillEligible && await HasRequiredApprovedSubmissionAsync(manual, ct) && usedUsers.Add(part!.UserId))
                    continue;
                ClearManualAwardAssignment(manual);
                _uow.Repository<EventAward>().Update(manual);
            }

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

                // Bỏ qua các suất đã chỉnh tay (kể cả suất Moderator chủ động bỏ trống).
                var autoSlots = tier.Slots.Where(s => !s.ManuallyAssignedAt.HasValue).ToList();
                for (var i = 0; i < autoSlots.Count; i++)
                {
                    if (i < filtered.Count)
                    {
                        autoSlots[i].ParticipationId = filtered[i].ParticipationId;
                        usedUsers.Add(filtered[i].UserId);
                    }
                    else
                    {
                        autoSlots[i].ParticipationId = null;
                    }
                    autoSlots[i].UpdatedAt = DateTime.UtcNow;
                    _uow.Repository<EventAward>().Update(autoSlots[i]);
                }
            }
        }
    }

    private static void ClearManualAwardAssignment(EventAward award)
    {
        award.ParticipationId = null;
        award.ManuallyAssignedAt = null;
        award.ManuallyAssignedByUserId = null;
        award.ManualAssignmentNote = null;
        award.UpdatedAt = DateTime.UtcNow;
    }

    private Task<bool> HasRequiredApprovedSubmissionAsync(EventAward award, CancellationToken ct) =>
        award.ResultSource is EventAwardResultSource.SubmissionScore or EventAwardResultSource.VotingAverage
            ? _uow.Repository<EventSubmission>().ExistsAsync(
                s => s.EventId == award.EventId && s.ParticipationId == award.ParticipationId &&
                    s.Status == EventSubmissionStatus.Approved && s.LockedStoryVersionId != null, ct)
            : Task.FromResult(true);

    private async Task<List<ActivityAwardRanking>> BuildActivityAwardRankingsSnapshotAsync(
        Event ev,
        IReadOnlyList<EventActivity> activities,
        EventLeaderboardDto? leaderboard,
        CancellationToken ct)
    {
        var rankings = new List<ActivityAwardRanking>();
        var eventId = ev.Id;
        var verifiedParts = await _uow.Repository<EventParticipation>().FindAsync(
            p => p.EventId == eventId && p.Status == EventParticipationStatus.Verified, cancellationToken: ct);
        var proposedAwards = await _uow.Repository<EventAward>().FindAsync(
            a => a.EventId == eventId && a.Status == EventAwardStatus.AwardProposed, cancellationToken: ct);
        if (proposedAwards.Count == 0) return rankings;

        var userIds = verifiedParts.Select(p => p.UserId).Distinct().ToList();
        var userRoles = userIds.Count == 0
            ? new Dictionary<int, UserRole>()
            : (await _uow.Repository<UserAccount>().FindAsync(u => userIds.Contains(u.Id), cancellationToken: ct))
                .ToDictionary(u => u.Id, u => u.Role);

        foreach (var sourceGroup in proposedAwards.GroupBy(RankingSourceKey))
        {
            var sourceSample = sourceGroup.First();
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
                rankings.Add(new ActivityAwardRanking(sourceSample.EventActivityId, tier.Tier,
                    orderedCandidates.Select(c => new ActivityAwardCandidate(c.ParticipationId, c.UserId)).ToArray()));
            }
        }
        return rankings;
    }

    public async Task<EventAwardSlotDto> ManualAssignAwardAsync(
        int moderatorUserId, int awardId, ManualAssignAwardRequestDto request, CancellationToken ct = default)
    {
        var initial = await _uow.Repository<EventAward>().FirstOrDefaultAsync(a => a.Id == awardId, cancellationToken: ct)
            ?? throw new StoryPlatform.Application.Common.Exceptions.NotFoundException(nameof(EventAward), awardId);
        return await InEventTransactionAsync(initial.EventId, async () =>
        {
            var ev = await _uow.Repository<Event>().GetByIdAsync(initial.EventId, ct)
                ?? throw new StoryPlatform.Application.Common.Exceptions.NotFoundException(nameof(Event), initial.EventId);
            if (ev.ConfigVersion != EventConfigVersion.ActivityBased)
                throw new StoryPlatform.Application.Common.Exceptions.ConflictException("Chỉ hỗ trợ chỉnh danh sách nhận giải cho sự kiện theo hoạt động.");
            EventModeratorAccess.RequireOwner(ev, moderatorUserId);
            var (_, activities) = await RequireEndedAsync(ev.Id, ct);
            if (ev.ResultsFinalizedAt.HasValue)
                throw new StoryPlatform.Application.Common.Exceptions.ConflictException("Danh sách người nhận giải đã được xác nhận, không thể chỉnh sửa.");

            var award = await _uow.Repository<EventAward>().GetByIdAsync(awardId, ct)
                ?? throw new StoryPlatform.Application.Common.Exceptions.NotFoundException(nameof(EventAward), awardId);
            if (award.Status != EventAwardStatus.AwardProposed)
                throw new StoryPlatform.Application.Common.Exceptions.ConflictException("Chỉ được chỉnh sửa suất giải đang ở trạng thái đề xuất.");

            var reason = request.Reason?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(reason))
                throw new StoryPlatform.Application.Common.Exceptions.BadRequestException("Cần nhập lý do khi chỉnh sửa người nhận giải.");
            if (reason.Length > 500)
                throw new StoryPlatform.Application.Common.Exceptions.BadRequestException("Lý do chỉnh sửa không được vượt quá 500 ký tự.");

            if (request.ParticipationId.HasValue)
            {
                await EnsureEligibleRecipientAsync(ev, award, activities, request.ParticipationId.Value, ct);
            }

            var before = new
            {
                award.ParticipationId,
                award.ManualAssignmentNote,
                award.ManuallyAssignedByUserId
            };
            var now = DateTime.UtcNow;
            award.ParticipationId = request.ParticipationId;
            award.ManualAssignmentNote = reason;
            award.ManuallyAssignedByUserId = moderatorUserId;
            award.ManuallyAssignedAt = now;
            award.UpdatedAt = now;
            _uow.Repository<EventAward>().Update(award);
            await _uow.SaveChangesAsync(ct);

            await _audit.LogAsync(moderatorUserId, "EventAwardManuallyAssigned", nameof(EventAward), award.Id, before,
                new { eventId = ev.Id, award.ParticipationId, Reason = reason }, ct);

            var activityMap = activities.ToDictionary(a => a.Id);
            var enriched = await EventAwardEnricher.MapForOperatorsAsync(_uow, ev.Id, [award], activityMap, ct);
            return enriched[0];
        }, ct);
    }

    private async Task<EventParticipation> EnsureEligibleRecipientAsync(
        Event ev,
        EventAward award,
        IReadOnlyList<EventActivity> activities,
        int participationId,
        CancellationToken ct)
    {
        var p = await _uow.Repository<EventParticipation>().GetByIdAsync(participationId, ct)
            ?? throw new StoryPlatform.Application.Common.Exceptions.NotFoundException(nameof(EventParticipation), participationId);
        if (p.EventId != ev.Id)
            throw new StoryPlatform.Application.Common.Exceptions.BadRequestException("Hồ sơ tham gia không thuộc sự kiện này.");
        if (p.Status != EventParticipationStatus.Verified)
            throw new StoryPlatform.Application.Common.Exceptions.ConflictException("Chỉ được gán giải cho người tham gia đã xác minh đạt.");

        var expectedActivity = ResolveSourceActivity(award, activities);
        if (expectedActivity == null || p.EventActivityId != expectedActivity.Id)
            throw new StoryPlatform.Application.Common.Exceptions.ConflictException("Hồ sơ tham gia không thuộc đúng hoạt động của suất giải này.");

        var user = await _uow.Repository<UserAccount>().GetByIdAsync(p.UserId, ct)
            ?? throw new StoryPlatform.Application.Common.Exceptions.NotFoundException(nameof(UserAccount), p.UserId);
        if (!IsUserMatchingAudience(user.Role, award.TargetAudience))
            throw new StoryPlatform.Application.Common.Exceptions.ConflictException("Người tham gia không thuộc đúng đối tượng của hạng giải này.");

        if (award.ResultSource is EventAwardResultSource.SubmissionScore or EventAwardResultSource.VotingAverage)
        {
            var hasApprovedSub = await _uow.Repository<EventSubmission>().ExistsAsync(
                s => s.EventId == ev.Id && s.ParticipationId == p.Id &&
                     s.Status == EventSubmissionStatus.Approved && s.LockedStoryVersionId != null, ct);
            if (!hasApprovedSub)
                throw new StoryPlatform.Application.Common.Exceptions.ConflictException("Người tham gia chưa có bài dự thi đạt kiểm duyệt cho suất giải này.");
        }

        var sourceKey = RankingSourceKey(award);
        var sameSourceAwards = await _uow.Repository<EventAward>().FindAsync(
            a => a.EventId == ev.Id && a.Id != award.Id && a.ParticipationId != null &&
                 a.Status != EventAwardStatus.Expired &&
                 a.Status != EventAwardStatus.UnclaimedBudgetReturned &&
                 a.Status != EventAwardStatus.Failed &&
                 a.Status != EventAwardStatus.Revoked,
            cancellationToken: ct);
        var otherPartIds = sameSourceAwards
            .Where(a => RankingSourceKey(a) == sourceKey)
            .Select(a => a.ParticipationId!.Value)
            .Distinct()
            .ToList();
        if (otherPartIds.Count > 0)
        {
            var otherParts = await _uow.Repository<EventParticipation>().FindAsync(
                x => otherPartIds.Contains(x.Id), cancellationToken: ct);
            if (otherParts.Any(x => x.UserId == p.UserId))
                throw new StoryPlatform.Application.Common.Exceptions.ConflictException("Người tham gia này đã giữ một suất giải khác thuộc cùng nguồn xếp hạng.");
        }

        return p;
    }

    public async Task<EventAwardSlotDto> RevokeAwardAsync(
        int moderatorUserId, int awardId, RevokeAwardRequestDto request, CancellationToken ct = default)
    {
        var initial = await _uow.Repository<EventAward>().FirstOrDefaultAsync(a => a.Id == awardId, cancellationToken: ct)
            ?? throw new StoryPlatform.Application.Common.Exceptions.NotFoundException(nameof(EventAward), awardId);

        var (resultDto, eventId, eventTitle, oldUserId, replacementUserId, rankOrTier, rewardName, reason, acceptedAppeal) =
            await InEventTransactionAsync(initial.EventId, async () =>
            {
                var ev = await _uow.Repository<Event>().GetByIdAsync(initial.EventId, ct)
                    ?? throw new StoryPlatform.Application.Common.Exceptions.NotFoundException(nameof(Event), initial.EventId);
                if (ev.ConfigVersion != EventConfigVersion.ActivityBased)
                    throw new StoryPlatform.Application.Common.Exceptions.ConflictException("Chỉ hỗ trợ thu hồi giải thưởng cho sự kiện theo hoạt động.");
                EventModeratorAccess.RequireOwner(ev, moderatorUserId);

                var trimmedReason = request.Reason?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(trimmedReason))
                    throw new StoryPlatform.Application.Common.Exceptions.BadRequestException("Cần nhập lý do khi thu hồi suất giải thưởng.");
                if (trimmedReason.Length > 500)
                    throw new StoryPlatform.Application.Common.Exceptions.BadRequestException("Lý do thu hồi không được vượt quá 500 ký tự.");

                var award = await _uow.Repository<EventAward>().GetByIdAsync(awardId, ct)
                    ?? throw new StoryPlatform.Application.Common.Exceptions.NotFoundException(nameof(EventAward), awardId);
                if (award.Status != EventAwardStatus.AppealHold || !award.ParticipationId.HasValue)
                    throw new StoryPlatform.Application.Common.Exceptions.ConflictException("Chỉ có thể thu hồi suất giải đang chờ hết hạn khiếu nại.");

                EventAppeal? appeal = null;
                if (request.AppealId.HasValue)
                {
                    appeal = await _uow.Repository<EventAppeal>().GetByIdAsync(request.AppealId.Value, ct);
                    if (appeal == null || appeal.EventId != ev.Id)
                        throw new StoryPlatform.Application.Common.Exceptions.NotFoundException(nameof(EventAppeal), request.AppealId.Value);
                    if (appeal.Status is not (EventAppealStatus.Open or EventAppealStatus.UnderReview or EventAppealStatus.Escalated))
                        throw new StoryPlatform.Application.Common.Exceptions.ConflictException("Đơn khiếu nại này đã có kết luận.");
                    if (appeal.TargetAwardId is { } targetAwardId && targetAwardId != award.Id)
                        throw new StoryPlatform.Application.Common.Exceptions.ConflictException("Đơn khiếu nại nhắm đến một suất giải khác.");
                }

                var now = DateTime.UtcNow;
                var inWindow = ev.ResultsPublishedAt.HasValue && now < ev.ResultsPublishedAt.Value.AddHours(EventAppealWindow.Hours);
                var hasOpenAppeals = await _uow.Repository<EventAppeal>().ExistsAsync(
                    a => a.EventId == ev.Id && (a.Status == EventAppealStatus.Open || a.Status == EventAppealStatus.UnderReview || a.Status == EventAppealStatus.Escalated), ct);
                if (!inWindow && !hasOpenAppeals)
                    throw new StoryPlatform.Application.Common.Exceptions.ConflictException("Đã hết hạn khiếu nại và không còn đơn đang mở, không thể thu hồi suất giải.");

                var activities = await _uow.Repository<EventActivity>().FindAsync(a => a.EventId == ev.Id, cancellationToken: ct);
                var oldPart = await _uow.Repository<EventParticipation>().GetByIdAsync(award.ParticipationId.Value, ct);
                int? prevUserId = oldPart?.UserId;

                // Chấp nhận đơn xác minh trước để người khiếu nại (nếu được chọn thay) đủ điều kiện "đã xác minh".
                if (appeal != null && appeal.Kind == EventAppealKind.Verification)
                {
                    var appellantPart = await _uow.Repository<EventParticipation>().GetByIdAsync(appeal.ParticipationId, ct);
                    if (appellantPart != null)
                    {
                        appellantPart.Status = EventParticipationStatus.Verified;
                        appellantPart.VerifiedAt = now;
                        appellantPart.UpdatedAt = now;
                        _uow.Repository<EventParticipation>().Update(appellantPart);
                    }
                }

                EventParticipation? replacementPart = null;
                if (request.ReplacementParticipationId.HasValue)
                {
                    replacementPart = await EnsureEligibleRecipientAsync(ev, award, activities, request.ReplacementParticipationId.Value, ct);
                    if (prevUserId.HasValue && replacementPart.UserId == prevUserId.Value)
                        throw new StoryPlatform.Application.Common.Exceptions.ConflictException("Người thay thế trùng với người vừa bị thu hồi suất giải.");
                }

                award.Status = EventAwardStatus.Revoked;
                award.RevokedReason = trimmedReason;
                award.RevokedByUserId = moderatorUserId;
                award.RevokedAt = now;
                award.UpdatedAt = now;
                _uow.Repository<EventAward>().Update(award);

                var returnedAward = award;
                if (replacementPart != null)
                {
                    var replacementAward = new EventAward
                    {
                        EventId = award.EventId,
                        EventActivityId = award.EventActivityId,
                        ResultSource = award.ResultSource,
                        TargetAudience = award.TargetAudience,
                        RankOrTier = award.RankOrTier,
                        SlotIndex = award.SlotIndex,
                        RewardType = award.RewardType,
                        RewardName = award.RewardName,
                        UnitValueVnd = award.UnitValueVnd,
                        CreditGrantAmount = award.CreditGrantAmount,
                        SubmitCreditGrantAmount = award.SubmitCreditGrantAmount,
                        ApprovedByAdminId = award.ApprovedByAdminId,
                        ParticipationId = replacementPart.Id,
                        ReallocatedFromAwardId = award.Id,
                        ReallocationRound = award.ReallocationRound + 1,
                        Status = EventAwardStatus.AppealHold,
                        CreatedAt = now
                    };
                    await _uow.Repository<EventAward>().AddAsync(replacementAward, ct);
                    returnedAward = replacementAward;
                }
                else
                {
                    var budgetReturn = CreateActivityAwardBudgetReturn(award);
                    await _uow.Repository<EventAward>().AddAsync(budgetReturn, ct);
                    await _uow.SaveChangesAsync(ct);
                    await _audit.LogAsync(moderatorUserId, "EventAwardBudgetReturned", nameof(EventAward), budgetReturn.Id,
                        null, new { eventId = ev.Id, RevokedAwardId = award.Id, award.UnitValueVnd }, ct);
                }

                if (appeal != null)
                {
                    appeal.Status = EventAppealStatus.Accepted;
                    appeal.ReviewedByUserId = moderatorUserId;
                    appeal.ResolutionNote = trimmedReason;
                    appeal.ResolvedAt = now;
                    appeal.UpdatedAt = now;
                    _uow.Repository<EventAppeal>().Update(appeal);
                }

                await _uow.SaveChangesAsync(ct);
                if (appeal != null)
                    await _audit.LogAsync(moderatorUserId, "EventAppealDecided", nameof(EventAppeal), appeal.Id, null,
                        new { appeal.Status, appeal.ResolutionNote, RevokedAwardId = award.Id }, ct);
                await _audit.LogAsync(moderatorUserId, "EventAwardRevoked", nameof(EventAward), award.Id,
                    new { award.ParticipationId, Status = EventAwardStatus.AppealHold },
                    new { eventId = ev.Id, Reason = trimmedReason, ReplacementAwardId = replacementPart != null ? returnedAward.Id : (int?)null, ReplacementParticipationId = replacementPart?.Id }, ct);

                var activityMap = activities.ToDictionary(a => a.Id);
                var enriched = await EventAwardEnricher.MapForOperatorsAsync(_uow, ev.Id, [returnedAward], activityMap, ct);
                (int AppellantUserId, int AppealId)? acceptedInfo = appeal == null ? null : (appeal.AppellantUserId, appeal.Id);
                return (enriched[0], ev.Id, ev.Title, prevUserId, replacementPart?.UserId, award.RankOrTier, award.RewardName, trimmedReason, acceptedInfo);
            }, ct);

        if (_notifications != null)
        {
            if (acceptedAppeal is { } accepted)
            {
                try
                {
                    var appealPayload = JsonSerializer.Serialize(new { eventId, eventTitle, appealId = accepted.AppealId, resolutionNote = reason });
                    await _notifications.CreateAsync(accepted.AppellantUserId, NotificationType.EventAppealAccepted, appealPayload, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _uow.DiscardTrackedChanges();
                }
            }
            if (oldUserId.HasValue)
            {
                try
                {
                    var revokePayload = JsonSerializer.Serialize(new { eventId, eventTitle, rankOrTier, rewardName, reason });
                    await _notifications.CreateAsync(oldUserId.Value, NotificationType.EventAwardRevoked, revokePayload, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _uow.DiscardTrackedChanges();
                }
            }
            if (replacementUserId.HasValue)
            {
                try
                {
                    var pubPayload = JsonSerializer.Serialize(new { eventId, eventTitle });
                    await _notifications.CreateAsync(replacementUserId.Value, NotificationType.EventResultPublished, pubPayload, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _uow.DiscardTrackedChanges();
                }
            }
        }

        return resultDto;
    }

    public async Task<IReadOnlyList<EventAwardCandidateDto>> GetAwardCandidatesAsync(
        int moderatorUserId, int awardId, CancellationToken ct = default)
    {
        var award = await _uow.Repository<EventAward>().FirstOrDefaultAsync(a => a.Id == awardId, cancellationToken: ct)
            ?? throw new StoryPlatform.Application.Common.Exceptions.NotFoundException(nameof(EventAward), awardId);
        var ev = await _uow.Repository<Event>().GetByIdAsync(award.EventId, ct)
            ?? throw new StoryPlatform.Application.Common.Exceptions.NotFoundException(nameof(Event), award.EventId);
        if (ev.ConfigVersion != EventConfigVersion.ActivityBased)
            throw new StoryPlatform.Application.Common.Exceptions.ConflictException("Chỉ hỗ trợ danh sách ứng viên cho sự kiện theo hoạt động.");
        EventModeratorAccess.RequireOwner(ev, moderatorUserId);

        var activities = await _uow.Repository<EventActivity>().FindAsync(a => a.EventId == ev.Id, cancellationToken: ct);
        var sourceActivity = ResolveSourceActivity(award, activities);
        if (sourceActivity == null) return Array.Empty<EventAwardCandidateDto>();

        var allParts = await _uow.Repository<EventParticipation>().FindAsync(p => p.EventId == ev.Id, cancellationToken: ct);
        var verifiedParts = allParts
            .Where(p => p.Status == EventParticipationStatus.Verified && p.EventActivityId == sourceActivity.Id)
            .ToList();
        if (verifiedParts.Count == 0) return Array.Empty<EventAwardCandidateDto>();

        var userIds = verifiedParts.Select(p => p.UserId).Distinct().ToList();
        var users = (await _uow.Repository<UserAccount>().FindAsync(u => userIds.Contains(u.Id), cancellationToken: ct))
            .ToDictionary(u => u.Id);
        var eligibleParts = verifiedParts
            .Where(p => users.TryGetValue(p.UserId, out var u) && IsUserMatchingAudience(u.Role, award.TargetAudience))
            .ToList();
        if (eligibleParts.Count == 0) return Array.Empty<EventAwardCandidateDto>();

        var partById = allParts.ToDictionary(p => p.Id);
        var sourceKey = RankingSourceKey(award);
        var allAwards = await _uow.Repository<EventAward>().FindAsync(
            a => a.EventId == ev.Id && a.ParticipationId != null &&
                 a.Status != EventAwardStatus.Expired &&
                 a.Status != EventAwardStatus.UnclaimedBudgetReturned &&
                 a.Status != EventAwardStatus.Failed &&
                 a.Status != EventAwardStatus.Revoked,
            cancellationToken: ct);
        var currentAwardByUser = new Dictionary<int, EventAward>();
        foreach (var existing in allAwards.Where(a => RankingSourceKey(a) == sourceKey))
        {
            if (partById.TryGetValue(existing.ParticipationId!.Value, out var holder) && !currentAwardByUser.ContainsKey(holder.UserId))
                currentAwardByUser[holder.UserId] = existing;
        }

        var candidates = new List<EventAwardCandidateDto>();
        if (award.ResultSource is EventAwardResultSource.SubmissionScore or EventAwardResultSource.VotingAverage)
        {
            var eligiblePartIds = eligibleParts.Select(p => p.Id).ToHashSet();
            var subs = await _uow.Repository<EventSubmission>().FindAsync(
                s => s.EventId == ev.Id && s.Status == EventSubmissionStatus.Approved &&
                     s.LockedStoryVersionId != null && eligiblePartIds.Contains(s.ParticipationId),
                cancellationToken: ct);
            var storyIds = subs.Select(s => s.StoryId).Distinct().ToList();
            var stories = storyIds.Count == 0
                ? new Dictionary<int, string?>()
                : (await _uow.Repository<Story>().FindAsync(s => storyIds.Contains(s.Id), cancellationToken: ct))
                    .ToDictionary(s => s.Id, s => (string?)s.Title);

            var subByPartId = subs
                .GroupBy(s => s.ParticipationId)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(s => s.SubmittedAt ?? s.CreatedAt).ThenByDescending(s => s.Id).First());

            if (award.ResultSource == EventAwardResultSource.SubmissionScore)
            {
                var evalCandidates = new List<(CreatorRankingCandidate Rank, int UserId, string? StoryTitle)>();
                var seenEvaluatedPartIds = new HashSet<int>();
                foreach (var sub in subs)
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
                    evalCandidates.Add((new CreatorRankingCandidate(sub.Id, p.Id, scores.Average(x => x.Total),
                        scores.Average(x => x.Primary), Math.Max(0, submittedCount - 1), sub.SubmittedAt ?? sub.CreatedAt),
                        p.UserId, stories.GetValueOrDefault(sub.StoryId)));
                    seenEvaluatedPartIds.Add(p.Id);
                }

                var metaBySub = evalCandidates.ToDictionary(x => x.Rank.SubmissionId, x => (x.UserId, x.StoryTitle));
                var rankedSubs = EventMissionEvaluator.RankCreatorSubmissions(evalCandidates.Select(x => x.Rank));
                var seenUsers = new HashSet<int>();
                foreach (var r in rankedSubs)
                {
                    var (uid, title) = metaBySub[r.SubmissionId];
                    if (!seenUsers.Add(uid)) continue;
                    currentAwardByUser.TryGetValue(uid, out var held);
                    candidates.Add(new EventAwardCandidateDto
                    {
                        ParticipationId = r.ParticipationId,
                        UserId = uid,
                        DisplayName = EventAwardEnricher.FormatDisplayName(users[uid]),
                        StoryTitle = title,
                        Rank = candidates.Count + 1,
                        RubricScore = Math.Round(r.TotalScore, 2),
                        CurrentAwardId = held?.Id,
                        CurrentAwardRankOrTier = held == null ? null : $"{held.RankOrTier} (Suất #{held.SlotIndex})"
                    });
                }

                foreach (var p in eligibleParts.Where(p => !seenEvaluatedPartIds.Contains(p.Id) && subByPartId.ContainsKey(p.Id))
                    .OrderBy(p => p.CreatedAt).ThenBy(p => p.Id))
                {
                    if (!seenUsers.Add(p.UserId)) continue;
                    currentAwardByUser.TryGetValue(p.UserId, out var held);
                    candidates.Add(new EventAwardCandidateDto
                    {
                        ParticipationId = p.Id,
                        UserId = p.UserId,
                        DisplayName = EventAwardEnricher.FormatDisplayName(users[p.UserId]),
                        StoryTitle = stories.GetValueOrDefault(subByPartId[p.Id].StoryId),
                        Rank = candidates.Count + 1,
                        CurrentAwardId = held?.Id,
                        CurrentAwardRankOrTier = held == null ? null : $"{held.RankOrTier} (Suất #{held.SlotIndex})"
                    });
                }
            }
            else
            {
                var lb = await new EventParticipationService(_uow, _audit).BuildLeaderboardAsync(ev.Id, includeHidden: true, verifiedOnly: true, ct);
                var seenUsers = new HashSet<int>();
                var entriesOrdered = lb.Entries
                    .Where(e => eligiblePartIds.Contains(e.ParticipationId))
                    .OrderByDescending(e => e.MeetsThreshold && e.Rank > 0)
                    .ThenBy(e => e.Rank > 0 ? e.Rank : int.MaxValue)
                    .ThenByDescending(e => e.AverageStars)
                    .ThenByDescending(e => e.ValidVoteCount);
                foreach (var entry in entriesOrdered)
                {
                    var p = partById[entry.ParticipationId];
                    if (!seenUsers.Add(p.UserId)) continue;
                    currentAwardByUser.TryGetValue(p.UserId, out var held);
                    candidates.Add(new EventAwardCandidateDto
                    {
                        ParticipationId = p.Id,
                        UserId = p.UserId,
                        DisplayName = EventAwardEnricher.FormatDisplayName(users[p.UserId]),
                        StoryTitle = string.IsNullOrWhiteSpace(entry.StoryTitle)
                            ? (subByPartId.TryGetValue(p.Id, out var s) ? stories.GetValueOrDefault(s.StoryId) : null)
                            : entry.StoryTitle,
                        Rank = candidates.Count + 1,
                        AverageStars = entry.AverageStars,
                        ValidVoteCount = entry.ValidVoteCount,
                        CurrentAwardId = held?.Id,
                        CurrentAwardRankOrTier = held == null ? null : $"{held.RankOrTier} (Suất #{held.SlotIndex})"
                    });
                }

                foreach (var p in eligibleParts.Where(p => subByPartId.ContainsKey(p.Id)).OrderBy(p => p.CreatedAt).ThenBy(p => p.Id))
                {
                    if (!seenUsers.Add(p.UserId)) continue;
                    currentAwardByUser.TryGetValue(p.UserId, out var held);
                    candidates.Add(new EventAwardCandidateDto
                    {
                        ParticipationId = p.Id,
                        UserId = p.UserId,
                        DisplayName = EventAwardEnricher.FormatDisplayName(users[p.UserId]),
                        StoryTitle = stories.GetValueOrDefault(subByPartId[p.Id].StoryId),
                        Rank = candidates.Count + 1,
                        AverageStars = 0,
                        ValidVoteCount = 0,
                        CurrentAwardId = held?.Id,
                        CurrentAwardRankOrTier = held == null ? null : $"{held.RankOrTier} (Suất #{held.SlotIndex})"
                    });
                }
            }
        }
        else if (award.ResultSource == EventAwardResultSource.MissionCompletion)
        {
            var seenUsers = new HashSet<int>();
            foreach (var p in eligibleParts.OrderBy(p => p.MissionCompletedAt ?? p.VerifiedAt ?? p.CreatedAt).ThenBy(p => p.Id))
            {
                if (!seenUsers.Add(p.UserId)) continue;
                currentAwardByUser.TryGetValue(p.UserId, out var held);
                candidates.Add(new EventAwardCandidateDto
                {
                    ParticipationId = p.Id,
                    UserId = p.UserId,
                    DisplayName = EventAwardEnricher.FormatDisplayName(users[p.UserId]),
                    Rank = candidates.Count + 1,
                    CompletedAt = p.MissionCompletedAt ?? p.VerifiedAt,
                    CurrentAwardId = held?.Id,
                    CurrentAwardRankOrTier = held == null ? null : $"{held.RankOrTier} (Suất #{held.SlotIndex})"
                });
            }
        }

        return candidates;
    }

    public Task<IReadOnlyList<EventAwardSlotDto>> ConfirmAwardsAsync(
        int moderatorUserId, int eventId, CancellationToken ct = default) =>
        InEventTransactionAsync(eventId, async () =>
        {
            var ev = await _uow.Repository<Event>().GetByIdAsync(eventId, ct)
                ?? throw new StoryPlatform.Application.Common.Exceptions.NotFoundException(nameof(Event), eventId);
            if (ev.ConfigVersion != EventConfigVersion.ActivityBased)
                throw new StoryPlatform.Application.Common.Exceptions.ConflictException("Chỉ hỗ trợ xác nhận danh sách nhận giải cho sự kiện theo hoạt động.");
            EventModeratorAccess.RequireOwner(ev, moderatorUserId);
            if (ev.ResultsFinalizedAt.HasValue)
                throw new StoryPlatform.Application.Common.Exceptions.ConflictException("Danh sách người nhận giải đã được xác nhận trước đó.");

            var (trackList, activityList) = await RequireEndedAsync(eventId, ct);
            var pending = await _uow.Repository<EventParticipation>().ExistsAsync(p => p.EventId == eventId &&
                (p.Status == EventParticipationStatus.Registered || p.Status == EventParticipationStatus.MissionCompleted ||
                 p.Status == EventParticipationStatus.PendingVerification || p.Status == EventParticipationStatus.ManualReview), ct);
            if (pending)
                throw new StoryPlatform.Application.Common.Exceptions.ConflictException("Cần xác minh xong người tham gia trước khi xác nhận danh sách nhận giải.");

            var openAppeals = await _uow.Repository<EventAppeal>().ExistsAsync(
                a => a.EventId == eventId && (a.Status == EventAppealStatus.Open || a.Status == EventAppealStatus.UnderReview || a.Status == EventAppealStatus.Escalated), ct);
            if (openAppeals)
                throw new StoryPlatform.Application.Common.Exceptions.ConflictException("Phải giải quyết hết toàn bộ đơn khiếu nại trước khi xác nhận danh sách nhận giải (BR-AP06).");

            var now = DateTime.UtcNow;
            await EnsureRubricEvaluationsCompleteAsync(ev, trackList, activityList, ct);

            // Tính lại danh sách tạm theo kết quả xác minh mới nhất (giữ nguyên suất Moderator đã chọn tay).
            await RecalculateActivityProposedAwardsInTransactionAsync(ev, activityList, ct);

            EventLeaderboardDto? snapshot = null;
            if (activityList.Any(a => a.ActivityType == EventActivityType.Voting))
            {
                snapshot = await new EventParticipationService(_uow, _audit).BuildLeaderboardAsync(ev.Id, includeHidden: true, verifiedOnly: true, ct);
                snapshot.ResultsFinalized = true;
                snapshot.FinalizedAt = now;
                ev.FinalLeaderboardSnapshotJson = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            }

            var rankings = await BuildActivityAwardRankingsSnapshotAsync(ev, activityList, snapshot, ct);
            ev.FinalAwardRankingSnapshotJson = JsonSerializer.Serialize(rankings);
            ev.ResultsFinalizedAt = now;
            ev.UpdatedAt = now;
            _uow.Repository<Event>().Update(ev);
            await _uow.SaveChangesAsync(ct);

            var awards = await _uow.Repository<EventAward>().FindAsync(a => a.EventId == eventId, cancellationToken: ct);
            await _audit.LogAsync(moderatorUserId, "EventAwardsConfirmed", nameof(Event), eventId, null,
                new
                {
                    AssignedCount = awards.Count(a => a.Status == EventAwardStatus.AwardProposed && a.ParticipationId.HasValue),
                    ManualCount = awards.Count(a => a.Status == EventAwardStatus.AwardProposed && a.ManuallyAssignedAt.HasValue),
                    UnassignedCount = awards.Count(a => a.Status == EventAwardStatus.AwardProposed && !a.ParticipationId.HasValue)
                }, ct);

            var activityMap = activityList.ToDictionary(a => a.Id);
            return (IReadOnlyList<EventAwardSlotDto>)await EventAwardEnricher.MapForOperatorsAsync(_uow, eventId, awards, activityMap, ct);
        }, ct);

    private static EventActivity? ResolveSourceActivity(EventAward award, IReadOnlyList<EventActivity> activities)
    {
        if (award.ResultSource is EventAwardResultSource.SubmissionScore or EventAwardResultSource.VotingAverage)
            return activities.FirstOrDefault(a => a.ActivityType == EventActivityType.Submission);
        if (award.ResultSource == EventAwardResultSource.MissionCompletion)
            return activities.FirstOrDefault(a => a.ActivityType == EventActivityType.ReadingMissions);
        return award.EventActivityId.HasValue ? activities.FirstOrDefault(a => a.Id == award.EventActivityId.Value) : null;
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
                await _uow.Repository<EventAward>().AddAsync(CreateActivityAwardBudgetReturn(oldAward), ct);
            }
        }

        await _uow.SaveChangesAsync(ct);
        await _audit.LogAsync(actorUserId, "EventExpiredClaimsProcessed", nameof(Event), ev.Id, null, new { ExpiredCount = expiredAwards.Count, Reallocated = reallocated }, ct);
        return reallocated;
    }

    private static EventAward CreateActivityAwardBudgetReturn(EventAward oldAward) => new()
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
    };
}
