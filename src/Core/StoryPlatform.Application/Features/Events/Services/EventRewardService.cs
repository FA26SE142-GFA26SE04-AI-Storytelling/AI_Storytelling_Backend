using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using StoryPlatform.Application.Abstractions.Persistence;
using Microsoft.Extensions.Logging;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Common.Time;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.Events.DTOs;
using StoryPlatform.Application.Features.Events.Interfaces;
using StoryPlatform.Application.Features.Notifications.DTOs;
using StoryPlatform.Application.Features.Notifications.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.Events.Services;

public partial class EventRewardService : IEventRewardService
{
    private readonly IUnitOfWork _uow;
    private readonly IAuditLogWriter _audit;
    private readonly INotificationService? _notifications;
    private readonly INotificationRealtimePublisher? _notificationPublisher;

    private readonly IRewardRecipientProtector? _recipientProtector;
    private readonly ILogger<EventRewardService>? _logger;

    public EventRewardService(IUnitOfWork uow, IAuditLogWriter audit, INotificationService? notifications = null,
        IRewardRecipientProtector? recipientProtector = null, ILogger<EventRewardService>? logger = null,
        INotificationRealtimePublisher? notificationPublisher = null)
    {
        _uow = uow;
        _audit = audit;
        _notifications = notifications;
        _notificationPublisher = notificationPublisher;
        _recipientProtector = recipientProtector;
        _logger = logger;
    }

    public async Task EvaluateSubmissionAsync(int evaluatorUserId, int submissionId, EvaluateSubmissionRequestDto request, CancellationToken ct = default)
    {
        var submission = await _uow.Repository<EventSubmission>().GetByIdAsync(submissionId, ct)
            ?? throw new NotFoundException(nameof(EventSubmission), submissionId);
        await InEventTransactionAsync(submission.EventId, async () =>
        {
            await EvaluateSubmissionLockedAsync(evaluatorUserId, submissionId, request, ct);
            return true;
        }, ct);
    }

    private async Task EvaluateSubmissionLockedAsync(int evaluatorUserId, int submissionId, EvaluateSubmissionRequestDto request, CancellationToken ct)
    {
        var submission = await _uow.Repository<EventSubmission>().GetByIdAsync(submissionId, ct)
            ?? throw new NotFoundException(nameof(EventSubmission), submissionId);
        if (submission.Status != EventSubmissionStatus.Approved || !submission.LockedStoryVersionId.HasValue)
            throw new ConflictException("Chỉ được chấm điểm bài dự thi đã vượt qua kiểm duyệt an toàn F-10.");
        var campaign = await _uow.Repository<Event>().GetByIdAsync(submission.EventId, ct)
            ?? throw new NotFoundException(nameof(Event), submission.EventId);
        if (campaign.ConfigVersion == EventConfigVersion.ActivityBased &&
            (campaign.Status != EventStatus.Published || campaign.ResultsFinalizedAt.HasValue))
            throw new ConflictException("Chỉ được chấm điểm khi sự kiện đã phát hành và kết quả chưa chốt.");
        if (campaign.ConfigVersion == EventConfigVersion.LegacyRoleBased && campaign.ScoringMode != EventScoringMode.Rubric)
            throw new ConflictException("Sự kiện này không chấm điểm theo tiêu chí.");
        await EventModeratorAccess.RequireOperatorAsync(_uow, campaign, evaluatorUserId, ct);
        var participation = await _uow.Repository<EventParticipation>().GetByIdAsync(submission.ParticipationId, ct)
            ?? throw new NotFoundException(nameof(EventParticipation), submission.ParticipationId);
        var rubric = await GetRubricAsync(participation, ct);
        var score = rubric.Score(request.CriteriaScoresJson)
            ?? throw new BadRequestException("Cần nhập điểm từ 0 đến 100 cho tất cả tiêu chí của thể lệ đã đăng ký.");

        var existing = await _uow.Repository<EventEvaluation>().FirstOrDefaultAsync(
            e => e.EventSubmissionId == submissionId && e.EvaluatorUserId == evaluatorUserId, cancellationToken: ct);
        if (existing == null)
        {
            await _uow.Repository<EventEvaluation>().AddAsync(new EventEvaluation
            {
                EventSubmissionId = submissionId,
                EvaluatorUserId = evaluatorUserId,
                CriteriaScoresJson = request.CriteriaScoresJson,
                TotalWeightedScore = score.Total,
                Comment = request.Comment,
                SubmittedAt = DateTime.UtcNow
            }, ct);
        }
        else
        {
            existing.CriteriaScoresJson = request.CriteriaScoresJson;
            existing.TotalWeightedScore = score.Total;
            existing.Comment = request.Comment;
            existing.UpdatedAt = DateTime.UtcNow;
            _uow.Repository<EventEvaluation>().Update(existing);
        }

        await _uow.SaveChangesAsync(ct);
    }

    public Task<IReadOnlyList<EventAwardSlotDto>> ProposeAwardsAsync(int moderatorUserId, int eventId, CancellationToken ct = default) =>
        InEventTransactionAsync(eventId, async () =>
        {
            var ev = await _uow.Repository<Event>().GetByIdAsync(eventId, ct)
                ?? throw new NotFoundException(nameof(Event), eventId);
            EventModeratorAccess.RequireOwner(ev, moderatorUserId);
            if (ev.ResultsFinalizedAt.HasValue)
                throw new ConflictException("Danh sách người nhận giải đã được xác nhận, không thể tính lại.");
            var (tracks, activities) = await RequireEndedAsync(eventId, ct);
            if (ev.ConfigVersion == EventConfigVersion.ActivityBased)
            {
                await RecalculateActivityProposedAwardsInTransactionAsync(ev, activities, ct);
            }
            else
            {
                await AssignProposedAwardsAsync(ev, tracks, await BuildVotingResultsAsync(ev, ct), ct);
            }
            await _uow.SaveChangesAsync(ct);
            var awards = await _uow.Repository<EventAward>().FindAsync(a => a.EventId == eventId, cancellationToken: ct);
            await _audit.LogAsync(moderatorUserId, "EventAwardsProposed", nameof(Event), eventId, null,
                new { Count = awards.Count(a => a.Status == EventAwardStatus.AwardProposed && a.ParticipationId.HasValue) }, ct);
            var activityMap = activities.ToDictionary(a => a.Id);
            return (IReadOnlyList<EventAwardSlotDto>)await EventAwardEnricher.MapForOperatorsAsync(_uow, eventId, awards, activityMap, ct);
        }, ct);

    private async Task AssignProposedAwardsAsync(
        Event ev, IReadOnlyList<EventTrack> tracks, EventLeaderboardDto? leaderboard, CancellationToken ct)
    {
        var eventId = ev.Id;
        var participations = await _uow.Repository<EventParticipation>().FindAsync(p => p.EventId == eventId && p.Status == EventParticipationStatus.Verified, cancellationToken: ct);
        var awards = await _uow.Repository<EventAward>().FindAsync(a => a.EventId == eventId && a.Status == EventAwardStatus.AwardProposed, cancellationToken: ct);
        var finalizedAwards = await _uow.Repository<EventAward>().FindAsync(a => a.EventId == eventId &&
            a.Status != EventAwardStatus.AwardProposed && a.ParticipationId != null, cancellationToken: ct);
        var alreadyAwarded = finalizedAwards.Select(a => a.ParticipationId!.Value).ToHashSet();

        foreach (var track in tracks)
        {
            var trackSlots = awards.Where(a => a.TrackId == track.Id).OrderBy(a => a.Id).ToList();
            if (trackSlots.Count == 0) continue;

            var eligible = participations.Where(p => p.TrackId == track.Id && !alreadyAwarded.Contains(p.Id)).ToList();
            var orderedParticipationIds = await OrderRecipientsAsync(ev, track.TrackType, track.AwardAllMissionCompleters, eligible, leaderboard, ct);

            for (var i = 0; i < trackSlots.Count; i++)
            {
                trackSlots[i].ParticipationId = i < orderedParticipationIds.Count ? orderedParticipationIds[i] : null;
                trackSlots[i].UpdatedAt = DateTime.UtcNow;
                _uow.Repository<EventAward>().Update(trackSlots[i]);
            }
        }

    }

    private async Task<(IReadOnlyList<EventTrack> Tracks, IReadOnlyList<EventActivity> Activities)> RequireEndedAsync(
        int eventId, CancellationToken ct, bool allowPaused = false)
    {
        var campaign = await _uow.Repository<Event>().GetByIdAsync(eventId, ct)
            ?? throw new NotFoundException(nameof(Event), eventId);
        var tracks = await _uow.Repository<EventTrack>().FindAsync(t => t.EventId == eventId, cancellationToken: ct);
        var activities = campaign.ConfigVersion == EventConfigVersion.ActivityBased
            ? await _uow.Repository<EventActivity>().FindAsync(a => a.EventId == eventId, cancellationToken: ct)
            : Array.Empty<EventActivity>();
        EventExecution.EnsureEnded(campaign, tracks, activities, allowPaused);
        return (tracks, activities);
    }

    private async Task<EventCreatorRubric> GetRubricAsync(EventParticipation participation, CancellationToken ct)
    {
        var rule = await _uow.Repository<EventRuleVersion>().GetByIdAsync(participation.AcceptedRuleVersionId, ct)
            ?? throw new ConflictException("Không tìm thấy phiên bản thể lệ đã đăng ký.");
        return EventCreatorRubric.Parse(rule.CreatorRubricJson);
    }

    private async Task<List<int>> RankCreatorAsync(IEnumerable<EventParticipation> participations, CancellationToken ct)
    {
        var candidates = new List<CreatorRankingCandidate>();
        foreach (var participation in participations)
        {
            var submission = await _uow.Repository<EventSubmission>().FirstOrDefaultAsync(s => s.ParticipationId == participation.Id &&
                s.Status == EventSubmissionStatus.Approved && s.LockedStoryVersionId != null, cancellationToken: ct);
            if (submission is null) continue;
            var evaluations = await _uow.Repository<EventEvaluation>().FindAsync(e => e.EventSubmissionId == submission.Id, cancellationToken: ct);
            if (evaluations.Count == 0) continue;
            var rubric = await GetRubricAsync(participation, ct);
            var scores = evaluations.Select(e => rubric.Score(e.CriteriaScoresJson)).OfType<(decimal Total, decimal Primary)>().ToList();
            if (scores.Count == 0) continue;
            var submittedCount = await _uow.Repository<AuditLog>().CountAsync(x => x.EntityType == nameof(EventSubmission) &&
                x.EntityId == submission.Id && x.Action == "EventStorySubmitted", ct);
            candidates.Add(new(submission.Id, participation.Id, scores.Average(x => x.Total), scores.Average(x => x.Primary),
                Math.Max(0, submittedCount - 1), submission.SubmittedAt ?? submission.CreatedAt));
        }
        return EventMissionEvaluator.RankCreatorSubmissions(candidates).Select(x => x.ParticipationId).ToList();
    }

    private async Task<EventLeaderboardDto?> BuildVotingResultsAsync(Event ev, CancellationToken ct) =>
        EventVoting.IsEnabled(ev.ScoringMode)
            ? await new EventParticipationService(_uow, _audit).BuildLeaderboardAsync(ev.Id, includeHidden: true, verifiedOnly: true, ct)
            : null;

    private async Task<List<int>> OrderRecipientsAsync(Event campaign, EventTrackType track, bool awardAllCompleters,
        List<EventParticipation> eligible, EventLeaderboardDto? leaderboard, CancellationToken ct)
    {
        if (leaderboard != null && !awardAllCompleters && EventVoting.RanksAuthors(campaign.ScoringMode, track))
        {
            var eligibleIds = eligible.Select(p => p.Id).ToHashSet();
            return leaderboard.Entries.Where(entry => entry.AuthorTrackType == track && entry.MeetsThreshold &&
                entry.Rank > 0 && eligibleIds.Contains(entry.ParticipationId))
                .OrderBy(entry => entry.Rank).Select(entry => entry.ParticipationId).ToList();
        }
        if (track == EventTrackType.Creator && campaign.ScoringMode == EventScoringMode.Rubric)
            return await RankCreatorAsync(eligible, ct);
        return eligible.OrderBy(p => p.MissionCompletedAt ?? p.VerifiedAt ?? p.CreatedAt)
            .ThenBy(p => p.Id).Select(p => p.Id).ToList();
    }

    private async Task<T> InEventTransactionAsync<T>(int eventId, Func<Task<T>> action, CancellationToken ct)
    {
        await _uow.BeginTransactionAsync(ct);
        try
        {
            await _uow.AcquireEventLockAsync(eventId, ct);
            var result = await action();
            await _uow.CommitTransactionAsync(ct);
            return result;
        }
        catch
        {
            await _uow.RollbackTransactionAsync(CancellationToken.None);
            _uow.DiscardTrackedChanges();
            throw;
        }
    }

    public async Task<IReadOnlyList<EventAwardSlotDto>> ApproveAwardsAsync(int adminUserId, int eventId, CancellationToken ct = default)
    {
        var (awards, isActivityBased, notices) = await InEventTransactionAsync(
            eventId, () => ApproveAwardsLockedAsync(adminUserId, eventId, ct), ct);
        if (isActivityBased)
        {
            await EventNotificationDelivery.PublishAsync(_notificationPublisher, notices, _logger);
            return awards;
        }
        var result = await NotifyAwardWinnersAsync(eventId, awards, adminUserId, ct);
        var failed = result.FailedAwardIds.ToHashSet();
        foreach (var award in awards)
            award.NotificationFailed = failed.Contains(award.Id);
        return awards;
    }

    public async Task<EventAwardNotificationResultDto> ResendAwardNotificationsAsync(
        int actorUserId, int eventId, CancellationToken ct = default)
    {
        var ev = await _uow.Repository<Event>().GetByIdAsync(eventId, ct)
            ?? throw new NotFoundException(nameof(Event), eventId);
        await EventModeratorAccess.RequireOperatorAsync(_uow, ev, actorUserId, ct);
        var now = DateTime.UtcNow;
        var awards = await _uow.Repository<EventAward>().FindAsync(a => a.EventId == eventId &&
            a.Status == EventAwardStatus.AwaitingClaim && a.ParticipationId != null &&
            (!a.ClaimDeadlineAt.HasValue || a.ClaimDeadlineAt.Value >= now), cancellationToken: ct);
        return await NotifyAwardWinnersAsync(eventId, awards.Select(MapAward), actorUserId, ct);
    }

    private async Task<EventAwardNotificationResultDto> NotifyAwardWinnersAsync(
        int eventId, IEnumerable<EventAwardSlotDto> awards, int actorUserId, CancellationToken ct)
    {
        var failed = new List<int>();
        var sent = 0;
        foreach (var award in awards.Where(a => a.ParticipationId.HasValue))
        {
            try
            {
                var part = await _uow.Repository<EventParticipation>().GetByIdAsync(award.ParticipationId!.Value, ct);
                if (_notifications == null || part == null)
                {
                    failed.Add(award.Id);
                    continue;
                }

                await _notifications.CreateAsync(part.UserId, NotificationType.EventAwardApproved,
                    JsonSerializer.Serialize(new { eventId, awardId = award.Id, award.RankOrTier, award.RewardName }), ct);
                sent++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _uow.DiscardTrackedChanges();
                failed.Add(award.Id);
                _logger?.LogWarning("Could not notify event {EventId} award {AwardId}: {ErrorType}",
                    eventId, award.Id, ex.GetType().Name);
                try
                {
                    await _audit.LogAsync(actorUserId, "EventAwardNotificationFailed", nameof(EventAward), award.Id,
                        null, new { eventId, awardId = award.Id }, ct);
                }
                catch (Exception auditError) when (auditError is not OperationCanceledException)
                {
                    _uow.DiscardTrackedChanges();
                    _logger?.LogWarning("Could not audit notification failure for award {AwardId}", award.Id);
                }
            }
        }
        return new(sent, failed);
    }

    private async Task<(IReadOnlyList<EventAwardSlotDto> Awards, bool IsActivityBased, List<(int UserId, NotificationDto Notice)> Notices)> ApproveAwardsLockedAsync(
        int adminUserId, int eventId, CancellationToken ct)
    {
        var ev = await _uow.Repository<Event>().GetByIdAsync(eventId, ct)
            ?? throw new NotFoundException(nameof(Event), eventId);
        var activityBased = ev.ConfigVersion == EventConfigVersion.ActivityBased;
        var publishedAt = ev.ResultsPublishedAt;
        if (activityBased && !publishedAt.HasValue && ev.ResultsFinalizedAt.HasValue)
        {
            var awardHistory = await _uow.Repository<EventAward>().FindAsync(
                a => a.EventId == eventId && a.ApprovedByAdminId != null && !a.IsDeleted, cancellationToken: ct);
            publishedAt = EventExecution.PublishedResultsAt(ev, awardHistory);
        }
        var (trackList, activityList) = await RequireEndedAsync(eventId, ct, allowPaused: activityBased && publishedAt.HasValue);
        var tracks = trackList.ToDictionary(t => t.Id);
        var activityMap = activityList.ToDictionary(a => a.Id);
        var openAppeals = await _uow.Repository<EventAppeal>().ExistsAsync(
            a => a.EventId == eventId && (a.Status == EventAppealStatus.Open || a.Status == EventAppealStatus.UnderReview || a.Status == EventAppealStatus.Escalated), ct);
        if (openAppeals)
            throw new ConflictException("Phải giải quyết hết toàn bộ đơn khiếu nại trước khi chốt danh sách trao giải (BR-AP06).");

        EventLeaderboardDto? snapshot = null;
        var votingEnabled = ev.ConfigVersion == EventConfigVersion.ActivityBased
            ? activityList.Any(a => a.ActivityType == EventActivityType.Voting)
            : EventVoting.IsEnabled(ev.ScoringMode);
        if (activityBased)
        {
            if (!ev.ResultsFinalizedAt.HasValue)
                throw new ConflictException("Moderator chưa xác nhận danh sách người nhận giải.");
        }
        else if (!ev.ResultsFinalizedAt.HasValue)
        {
            var pending = await _uow.Repository<EventParticipation>().ExistsAsync(p => p.EventId == eventId &&
                (p.Status == EventParticipationStatus.Registered || p.Status == EventParticipationStatus.MissionCompleted ||
                 p.Status == EventParticipationStatus.PendingVerification || p.Status == EventParticipationStatus.ManualReview), ct);
            if (pending)
                throw new ConflictException("Cần xác minh xong người tham gia trước khi chốt kết quả.");
            await EventAppealWindow.EnsureNoOpenWindowAsync(_uow, eventId, DateTime.UtcNow, ct);
            await EnsureRubricEvaluationsCompleteAsync(ev, trackList, activityList, ct);
            if (votingEnabled)
                snapshot = await BuildVotingResultsAsync(ev, ct);
            await AssignProposedAwardsAsync(ev, trackList, snapshot, ct);
            await _uow.SaveChangesAsync(ct);
        }

        var proposedAwards = await _uow.Repository<EventAward>().FindAsync(
            a => a.EventId == eventId && a.Status == EventAwardStatus.AwardProposed && a.ParticipationId != null, cancellationToken: ct);
        var awards = new List<EventAward>(proposedAwards.Count);
        foreach (var award in proposedAwards)
        {
            if (activityBased && award.ManuallyAssignedAt.HasValue)
            {
                var part = await _uow.Repository<EventParticipation>().GetByIdAsync(award.ParticipationId!.Value, ct);
                var user = part is null ? null : await _uow.Repository<UserAccount>().GetByIdAsync(part.UserId, ct);
                if (part is null || part.IsDeleted || part.EventId != eventId || part.Status != EventParticipationStatus.Verified ||
                    !part.EventActivityId.HasValue || part.EventActivityId != ResolveSourceActivity(award, activityList)?.Id ||
                    user is null || user.IsDeleted || !IsUserMatchingAudience(user.Role, award.TargetAudience) ||
                    !await HasRequiredApprovedSubmissionAsync(award, ct))
                {
                    ClearManualAwardAssignment(award);
                    _uow.Repository<EventAward>().Update(award);
                    continue;
                }
            }
            awards.Add(award);
        }

        var firstPublication = activityBased && !publishedAt.HasValue;
        var notices = new List<(int UserId, NotificationDto Notice)>();
        if (activityBased && !firstPublication && proposedAwards.Count == 0)
            return (Array.Empty<EventAwardSlotDto>(), true, notices);

        var now = DateTime.UtcNow;
        foreach (var award in awards)
        {
            if (activityBased && (firstPublication || ev.ResultsPublishedAt.HasValue))
            {
                award.Status = EventAwardStatus.AppealHold;
                award.ApprovedByAdminId = adminUserId;
                award.ClaimDeadlineAt = null;
                award.UpdatedAt = now;
            }
            else
            {
                var hours = activityBased && award.EventActivityId is { } activityId && activityMap.TryGetValue(activityId, out var activity)
                    ? activity.ClaimWindowHours
                    : award.TrackId.HasValue && tracks.TryGetValue(award.TrackId.Value, out var tr) ? tr.ClaimWindowHours : 72;
                award.Status = EventAwardStatus.AwaitingClaim;
                award.ApprovedByAdminId = adminUserId;
                award.ClaimDeadlineAt = now.AddHours(hours);
                award.UpdatedAt = now;
            }
            _uow.Repository<EventAward>().Update(award);
        }

        if (snapshot is not null)
        {
            snapshot.ResultsFinalized = true;
            snapshot.FinalizedAt = ev.ResultsFinalizedAt ?? now;
            ev.FinalLeaderboardSnapshotJson = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }

        ev.ResultsFinalizedAt ??= now;
        if (firstPublication)
            ev.ResultsPublishedAt = now;
        ev.UpdatedAt = now;
        _uow.Repository<Event>().Update(ev);

        await _uow.SaveChangesAsync(ct);
        var unassignedCount = await _uow.Repository<EventAward>().CountAsync(
            a => a.EventId == eventId && a.Status == EventAwardStatus.AwardProposed && a.ParticipationId == null, ct);
        await _audit.LogAsync(adminUserId, "EventAwardsApproved", nameof(Event), eventId, null,
            new { ApprovedCount = awards.Count, UnassignedCount = unassignedCount, Published = activityBased }, ct);

        if (activityBased && _notifications != null)
        {
            var awardedPartIds = awards.Select(a => a.ParticipationId!.Value).ToHashSet();
            var participants = await _uow.Repository<EventParticipation>().FindAsync(
                p => p.EventId == eventId && p.EventActivityId != null && !p.IsDeleted &&
                    (firstPublication || awardedPartIds.Contains(p.Id)), cancellationToken: ct);
            if (ev.ResultsPublishedAt.HasValue)
            {
                var payload = JsonSerializer.Serialize(new { eventId, eventTitle = ev.Title });
                foreach (var uid in participants.Select(p => p.UserId).Distinct())
                    notices.Add((uid, await StoreApprovalNoticeAsync(uid, NotificationType.EventResultPublished, payload, ct)));
            }
            else
            {
                var recipientByPart = participants.ToDictionary(p => p.Id, p => p.UserId);
                foreach (var award in awards)
                {
                    if (!recipientByPart.TryGetValue(award.ParticipationId!.Value, out var uid)) continue;
                    var payload = JsonSerializer.Serialize(new { eventId, awardId = award.Id, rankOrTier = award.RankOrTier, rewardName = award.RewardName });
                    notices.Add((uid, await StoreApprovalNoticeAsync(uid, NotificationType.EventAwardApproved, payload, ct)));
                }
            }
        }

        var mapped = await EventAwardEnricher.MapForOperatorsAsync(_uow, eventId, awards, activityMap, ct);
        return (mapped, activityBased, notices);
    }

    private async Task<NotificationDto> StoreApprovalNoticeAsync(int userId, NotificationType type, string payload, CancellationToken ct)
    {
        try
        {
            return await _notifications!.CreateStoredAsync(userId, type, payload, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(ex, "Could not store event approval notification of type {NotificationType}", type);
            throw new ApprovalNotificationException();
        }
    }

    private sealed class ApprovalNotificationException()
        : AppException("Chưa thể duyệt giải vì không lưu được thông báo. Vui lòng thử lại.", 503, "EVENT_APPROVAL_NOTIFICATION_UNAVAILABLE");

    private async Task EnsureRubricEvaluationsCompleteAsync(
        Event ev, IReadOnlyList<EventTrack> tracks, IReadOnlyList<EventActivity> activities, CancellationToken ct)
    {
        var scopeIds = ev.ConfigVersion == EventConfigVersion.ActivityBased
            ? activities.Where(a => a.ActivityType == EventActivityType.Submission).Select(a => a.Id).ToList()
            : tracks.Where(t => t.TrackType == EventTrackType.Creator).Select(t => t.Id).ToList();
        if (scopeIds.Count == 0)
            return;

        var activityBased = ev.ConfigVersion == EventConfigVersion.ActivityBased;
        if (activityBased
            ? !await _uow.Repository<EventAward>().ExistsAsync(a => a.EventId == ev.Id &&
                a.ResultSource == EventAwardResultSource.SubmissionScore, ct)
            : ev.ScoringMode != EventScoringMode.Rubric)
            return;

        var participations = await _uow.Repository<EventParticipation>().FindAsync(p => p.EventId == ev.Id &&
            p.Status != EventParticipationStatus.Disqualified &&
            (activityBased ? p.EventActivityId.HasValue && scopeIds.Contains(p.EventActivityId.Value) :
                p.TrackId.HasValue && scopeIds.Contains(p.TrackId.Value)), cancellationToken: ct);
        var partIds = participations.Select(p => p.Id).ToList();
        var submissions = await _uow.Repository<EventSubmission>().FindAsync(s => s.EventId == ev.Id &&
            partIds.Contains(s.ParticipationId) && s.Status == EventSubmissionStatus.Approved &&
            s.LockedStoryVersionId.HasValue, cancellationToken: ct);
        var submissionIds = submissions.Select(s => s.Id).ToList();
        if (submissionIds.Count == 0)
            return;

        var evaluations = await _uow.Repository<EventEvaluation>().FindAsync(
            e => submissionIds.Contains(e.EventSubmissionId), cancellationToken: ct);
        if (evaluations.Select(e => e.EventSubmissionId).Distinct().Count() < submissionIds.Count)
            throw new ConflictException("Còn bài dự thi chưa được chấm điểm; cần chấm xong trước khi chốt giải.");
    }

    public async Task<IReadOnlyList<EventAwardSlotDto>> GetMyRewardsAsync(int userId, CancellationToken ct = default)
    {
        var myParts = await _uow.Repository<EventParticipation>().FindAsync(p => p.UserId == userId, cancellationToken: ct);
        var partIds = myParts.Select(p => p.Id).ToHashSet();
        if (partIds.Count == 0) return Array.Empty<EventAwardSlotDto>();

        var awards = await _uow.Repository<EventAward>().FindAsync(
            a => a.ParticipationId.HasValue && partIds.Contains(a.ParticipationId.Value) &&
                !a.IsDeleted && a.Status != EventAwardStatus.AwardProposed, cancellationToken: ct);
        return awards.Select(MapAward).ToList();
    }

    public async Task<EventAwardSlotDto> ClaimRewardAsync(int userId, int awardId, ClaimRewardRequestDto request, CancellationToken ct = default)
    {
        var award = await _uow.Repository<EventAward>().FirstOrDefaultAsync(
            a => a.Id == awardId, cancellationToken: ct)
            ?? throw new NotFoundException(nameof(EventAward), awardId);
        return await InEventTransactionAsync(award.EventId, () => ClaimRewardLockedAsync(userId, awardId, request, ct), ct);
    }

    private async Task<EventAwardSlotDto> ClaimRewardLockedAsync(int userId, int awardId, ClaimRewardRequestDto request, CancellationToken ct)
    {
        var award = await _uow.Repository<EventAward>().GetByIdAsync(awardId, ct)
            ?? throw new NotFoundException(nameof(EventAward), awardId);
        if (award.Status == EventAwardStatus.AppealHold)
        {
            var evHold = await _uow.Repository<Event>().GetByIdAsync(award.EventId, ct);
            if (evHold?.ResultsPublishedAt is { } publishedAt)
            {
                var opensAt = publishedAt.AddHours(EventAppealWindow.Hours);
                throw new ConflictException($"Giải sẽ mở nhận từ {opensAt + VietnamTime.Offset:HH:mm dd/MM/yyyy} sau khi hết hạn khiếu nại.");
            }
            throw new ConflictException("Giải sẽ mở nhận sau khi hết hạn khiếu nại.");
        }
        if (award.Status != EventAwardStatus.AwaitingClaim || !award.ParticipationId.HasValue)
            throw new ConflictException("Giải thưởng không ở trạng thái chờ xác nhận nhận giải.");

        var participation = await _uow.Repository<EventParticipation>().GetByIdAsync(award.ParticipationId.Value, ct)
            ?? throw new NotFoundException(nameof(EventParticipation), award.ParticipationId.Value);
        if (participation.UserId != userId)
            throw new ForbiddenException("Bạn không phải người trúng suất giải thưởng này.");
        if (award.ManuallyAssignedAt.HasValue &&
            (participation.Status != EventParticipationStatus.Verified || !await HasRequiredApprovedSubmissionAsync(award, ct)))
            throw new ConflictException("Hồ sơ được chỉ định thủ công hiện không còn đủ điều kiện nhận giải.");

        var now = DateTime.UtcNow;
        if (award.ClaimDeadlineAt.HasValue && now > award.ClaimDeadlineAt.Value)
            throw new ConflictException("Đã quá thời hạn xác nhận nhận giải.");

        if (award.RewardType == EventRewardType.PlatformCredits)
        {
            await _uow.Repository<CreditLot>().AddAsync(new CreditLot
            {
                UserId = userId,
                CreditType = CreditType.AI,
                Source = CreditSource.EventReward,
                Total = award.CreditGrantAmount,
                Remaining = award.CreditGrantAmount,
                ExpiresAt = null,
                EventId = award.EventId
            }, ct);
            award.Status = EventAwardStatus.Fulfilled;
        }
        else if (award.RewardType == EventRewardType.CreatorPackage)
        {
            var ev = await _uow.Repository<Event>().GetByIdAsync(award.EventId, ct);
            if (ev?.ConfigVersion == EventConfigVersion.ActivityBased)
            {
                var account = await _uow.Repository<UserAccount>().GetByIdAsync(userId, ct);
                if (account?.Role != UserRole.ContentCreator || award.TargetAudience != EventAudience.Creator ||
                    participation.EventId != award.EventId)
                    throw new ConflictException("Gói Creator chỉ dành cho tài khoản Creator.");
            }
            else
            {
                var track = award.TrackId.HasValue ? await _uow.Repository<EventTrack>().GetByIdAsync(award.TrackId.Value, ct) : null;
                if (track?.TrackType != EventTrackType.Creator || track.EventId != award.EventId ||
                    participation.TrackId != track.Id || participation.EventId != award.EventId)
                    throw new ConflictException("Gói Creator chỉ dành cho người tham gia nhánh Creator.");
            }

            if (award.CreditGrantAmount > 0)
            {
                await _uow.Repository<CreditLot>().AddAsync(new CreditLot
                {
                    UserId = userId,
                    CreditType = CreditType.AI,
                    Source = CreditSource.EventReward,
                    Total = award.CreditGrantAmount,
                    Remaining = award.CreditGrantAmount,
                    ExpiresAt = null,
                    EventId = award.EventId
                }, ct);
            }
            if (award.SubmitCreditGrantAmount > 0)
            {
                await _uow.Repository<CreditLot>().AddAsync(new CreditLot
                {
                    UserId = userId,
                    CreditType = CreditType.Submit,
                    Source = CreditSource.EventReward,
                    Total = award.SubmitCreditGrantAmount,
                    Remaining = award.SubmitCreditGrantAmount,
                    ExpiresAt = null,
                    EventId = award.EventId
                }, ct);
            }
            award.Status = EventAwardStatus.Fulfilled;
        }
        else
        {
            object piiPayload;
            if (award.RewardType == EventRewardType.PhysicalGift)
            {
                var name = request.RecipientName?.Trim() ?? string.Empty;
                var phone = request.PhoneNumber?.Trim() ?? string.Empty;
                var address = request.ShippingAddress?.Trim() ?? string.Empty;
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(phone) || string.IsNullOrEmpty(address))
                    throw new BadRequestException("Cần nhập họ tên, số điện thoại và địa chỉ nhận quà.");
                piiPayload = new { RecipientName = name, PhoneNumber = phone, ShippingAddress = address };
            }
            else if (award.RewardType == EventRewardType.CashTransfer)
            {
                var payout = await _uow.Repository<CreatorPayoutAccount>().FirstOrDefaultAsync(
                    x => x.CreatorUserId == userId, cancellationToken: ct);
                var bank = request.BankName?.Trim() ?? string.Empty;
                var number = request.BankAccountNumber?.Trim() ?? string.Empty;
                var holder = request.BankAccountHolder?.Trim() ?? string.Empty;
                if (bank.Length == 0 && number.Length == 0 && holder.Length == 0 && payout != null)
                {
                    bank = payout.BankName;
                    number = payout.AccountNumber;
                    holder = payout.AccountHolderName;
                }
                if (string.IsNullOrWhiteSpace(bank) || string.IsNullOrWhiteSpace(number) || string.IsNullOrWhiteSpace(holder))
                    throw new BadRequestException("Cần nhập ngân hàng, số tài khoản và tên chủ tài khoản.");
                if (bank.Length > 200 || number.Length > 100 || holder.Length > 200)
                    throw new BadRequestException("Thông tin tài khoản nhận tiền vượt quá độ dài cho phép.");
                piiPayload = new { BankName = bank, BankAccountNumber = number, BankAccountHolder = holder };
            }
            else
            {
                piiPayload = new { request.RecipientName, request.PhoneNumber, request.ShippingAddress };
            }

            await _uow.Repository<RewardFulfillment>().AddAsync(new RewardFulfillment
            {
                EventAwardId = award.Id,
                AttemptNo = 1,
                RecipientInfoProtectedJson = RequireRecipientProtector().Protect(JsonSerializer.Serialize(piiPayload)),
                Status = RewardFulfillmentStatus.Processing
            }, ct);
            award.Status = EventAwardStatus.FulfillmentProcessing;
        }

        if (award.ResultSource == EventAwardResultSource.MissionCompletion)
        {
            var claimer = await _uow.Repository<UserAccount>().GetByIdAsync(userId, ct);
            if (claimer?.Role == UserRole.User)
                award.ShowChildNames = request.ShowChildNames == true;
        }

        award.UpdatedAt = now;
        _uow.Repository<EventAward>().Update(award);
        await _uow.SaveChangesAsync(ct);

        await _audit.LogAsync(userId, "EventRewardClaimed", nameof(EventAward), award.Id, null, new { award.RewardType, award.Status }, ct);
        return MapAward(award);
    }

    public Task<int> ProcessExpiredClaimsAsync(int actorUserId, int eventId, CancellationToken ct = default) =>
        InEventTransactionAsync(eventId, () => ProcessExpiredClaimsLockedAsync(actorUserId, eventId, ct), ct);

    private async Task<int> ProcessExpiredClaimsLockedAsync(int actorUserId, int eventId, CancellationToken ct)
    {
        var ev = await _uow.Repository<Event>().GetByIdAsync(eventId, ct)
            ?? throw new NotFoundException(nameof(Event), eventId);
        await EventModeratorAccess.RequireOperatorAsync(_uow, ev, actorUserId, ct);
        var now = DateTime.UtcNow;
        var expiredAwards = await _uow.Repository<EventAward>().FindAsync(
            a => a.EventId == eventId && a.Status == EventAwardStatus.AwaitingClaim && a.ClaimDeadlineAt != null && a.ClaimDeadlineAt < now, cancellationToken: ct);

        if (expiredAwards.Count == 0) return 0;

        if (ev.ConfigVersion == EventConfigVersion.ActivityBased)
        {
            var activities = await _uow.Repository<EventActivity>().FindAsync(a => a.EventId == eventId, cancellationToken: ct);
            return await ProcessActivityExpiredClaimsLockedAsync(actorUserId, ev, activities, expiredAwards, ct);
        }

        var tracks = (await _uow.Repository<EventTrack>().FindAsync(t => t.EventId == eventId, cancellationToken: ct)).ToDictionary(t => t.Id);
        var votingResults = await BuildVotingResultsAsync(ev, ct);
        var allEventAwards = await _uow.Repository<EventAward>().FindAsync(a => a.EventId == eventId, cancellationToken: ct);
        var usedParticipationIds = allEventAwards.Where(a => a.ParticipationId.HasValue).Select(a => a.ParticipationId!.Value).ToHashSet();
        var verifiedParticipations = await _uow.Repository<EventParticipation>().FindAsync(
            p => p.EventId == eventId && p.Status == EventParticipationStatus.Verified, cancellationToken: ct);

        var reallocated = 0;
        foreach (var oldAward in expiredAwards)
        {
            oldAward.Status = EventAwardStatus.Expired;
            oldAward.UpdatedAt = now;
            _uow.Repository<EventAward>().Update(oldAward);

            EventTrack? tr = null;
            var maxRounds = oldAward.TrackId.HasValue && tracks.TryGetValue(oldAward.TrackId.Value, out tr) ? tr.MaxReallocationRounds : 1;
            var eligible = verifiedParticipations.Where(p => p.TrackId == oldAward.TrackId && !usedParticipationIds.Contains(p.Id)).ToList();
            EventParticipation? runnerUp = null;
            if (oldAward.ReallocationRound < maxRounds)
            {
                var ranking = await OrderRecipientsAsync(ev, tr?.TrackType ?? EventTrackType.Parent,
                    tr?.AwardAllMissionCompleters == true, eligible, votingResults, ct);
                var nextId = ranking.FirstOrDefault();
                runnerUp = eligible.FirstOrDefault(p => p.Id == nextId);
            }

            if (runnerUp != null)
            {
                usedParticipationIds.Add(runnerUp.Id);
                await _uow.Repository<EventAward>().AddAsync(new EventAward
                {
                    EventId = oldAward.EventId,
                    TrackId = oldAward.TrackId,
                    RankOrTier = oldAward.RankOrTier,
                    SlotIndex = oldAward.SlotIndex,
                    RewardType = oldAward.RewardType,
                    RewardName = oldAward.RewardName,
                    UnitValueVnd = oldAward.UnitValueVnd,
                    CreditGrantAmount = oldAward.CreditGrantAmount,
                    SubmitCreditGrantAmount = oldAward.SubmitCreditGrantAmount,
                    ParticipationId = runnerUp.Id,
                    ReallocatedFromAwardId = oldAward.Id,
                    ReallocationRound = oldAward.ReallocationRound + 1,
                    Status = EventAwardStatus.AwardProposed
                }, ct);
                reallocated++;
            }
            else
            {
                await _uow.Repository<EventAward>().AddAsync(new EventAward
                {
                    EventId = oldAward.EventId,
                    TrackId = oldAward.TrackId,
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
        await _audit.LogAsync(actorUserId, "EventExpiredClaimsProcessed", nameof(Event), eventId, null, new { ExpiredCount = expiredAwards.Count, Reallocated = reallocated }, ct);
        return reallocated;
    }

    private IRewardRecipientProtector RequireRecipientProtector() =>
        _recipientProtector ?? throw new InvalidOperationException("Chưa cấu hình bảo vệ thông tin nhận giải.");

    public async Task<EventRewardRecipientDto> GetRewardRecipientAsync(int operatorUserId, int awardId,
        CancellationToken ct = default)
    {
        var award = await _uow.Repository<EventAward>().FirstOrDefaultAsync(a => a.Id == awardId, cancellationToken: ct)
            ?? throw new NotFoundException(nameof(EventAward), awardId);
        return await InEventTransactionAsync(award.EventId, async () =>
        {
            var ev = await _uow.Repository<Event>().GetByIdAsync(award.EventId, ct)
                ?? throw new NotFoundException(nameof(Event), award.EventId);
            await EventModeratorAccess.RequireOperatorAsync(_uow, ev, operatorUserId, ct);

            var attempts = await _uow.Repository<RewardFulfillment>().FindAsync(
                x => x.EventAwardId == awardId && x.RecipientInfoProtectedJson != null, cancellationToken: ct);
            var stored = attempts.OrderByDescending(x => x.AttemptNo).FirstOrDefault()?.RecipientInfoProtectedJson;
            if (string.IsNullOrWhiteSpace(stored))
                throw new NotFoundException("Thông tin nhận giải", awardId);

            // Chỉ dữ liệu cũ thực sự là đối tượng JSON mới được đọc dưới dạng rõ.
            var legacy = stored.TrimStart().StartsWith('{');
            string json = stored;
            if (!legacy && !RequireRecipientProtector().TryUnprotect(stored, out json))
                throw new ConflictException("Không thể giải mã thông tin nhận giải.");

            EventRewardRecipientDto recipient;
            try
            {
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    throw new JsonException();
                string? Read(string property) =>
                    root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
                        ? value.GetString() : null;

                recipient = new()
                {
                    AwardId = award.Id,
                    RewardType = award.RewardType,
                    RecipientName = Read("RecipientName"),
                    PhoneNumber = Read("PhoneNumber"),
                    ShippingAddress = award.RewardType == EventRewardType.PhysicalGift ? Read("ShippingAddress") : null,
                    BankName = Read("BankName"),
                    BankAccountNumber = Read("BankAccountNumber") ?? Read("AccountNumber"),
                    BankAccountHolder = Read("BankAccountHolder") ?? Read("AccountHolderName"),
                    LegacyBankAccountText = legacy && award.RewardType == EventRewardType.CashTransfer
                        ? Read("BankAccountText") ?? Read("ShippingAddress") : null,
                    IsLegacyPlaintext = legacy
                };
            }
            catch (JsonException)
            {
                throw new ConflictException("Thông tin nhận giải không đúng định dạng.");
            }

            await _audit.LogAsync(operatorUserId, "EventRewardRecipientViewed", nameof(EventAward), award.Id, null,
                new { eventId = ev.Id, isLegacyPlaintext = legacy }, ct);
            return recipient;
        }, ct);
    }

    public async Task<EventAwardSlotDto> UpdateFulfillmentAsync(int operatorUserId, int awardId, FulfillRewardRequestDto request, CancellationToken ct = default)
    {
        var award = await _uow.Repository<EventAward>().FirstOrDefaultAsync(a => a.Id == awardId, cancellationToken: ct)
            ?? throw new NotFoundException(nameof(EventAward), awardId);
        return await InEventTransactionAsync(award.EventId, () => UpdateFulfillmentLockedAsync(operatorUserId, awardId, request, ct), ct);
    }

    private async Task<EventAwardSlotDto> UpdateFulfillmentLockedAsync(int operatorUserId, int awardId, FulfillRewardRequestDto request, CancellationToken ct)
    {
        var award = await _uow.Repository<EventAward>().GetByIdAsync(awardId, ct)
            ?? throw new NotFoundException(nameof(EventAward), awardId);
        if (award.Status is not (EventAwardStatus.FulfillmentProcessing or EventAwardStatus.Failed) || !award.ParticipationId.HasValue)
            throw new ConflictException("Chỉ có thể cập nhật suất giải đang được trao hoặc cần trao lại sau khi người thắng xác nhận.");
        var campaign = await _uow.Repository<Event>().GetByIdAsync(award.EventId, ct)
            ?? throw new NotFoundException(nameof(Event), award.EventId);
        await EventModeratorAccess.RequireOperatorAsync(_uow, campaign, operatorUserId, ct);
        if (campaign.Status is EventStatus.Closed or EventStatus.Archived)
            throw new ConflictException("Không thể cập nhật trao giải sau khi sự kiện đã đóng.");

        var attempts = await _uow.Repository<RewardFulfillment>().FindAsync(f => f.EventAwardId == awardId, cancellationToken: ct);
        var nextAttempt = attempts.Count == 0 ? 1 : attempts.Max(a => a.AttemptNo) + 1;
        var last = attempts.OrderByDescending(a => a.AttemptNo).FirstOrDefault();

        if (last != null && last.Status == RewardFulfillmentStatus.Processing)
        {
            last.Status = request.Delivered ? RewardFulfillmentStatus.Delivered : RewardFulfillmentStatus.Failed;
            last.TrackingOrBankTxCode = request.TrackingOrBankTxCode;
            last.FailureReason = request.FailureReason;
            last.HandledByUserId = operatorUserId;
            last.DeliveredAt = request.Delivered ? DateTime.UtcNow : null;
            last.UpdatedAt = DateTime.UtcNow;
            _uow.Repository<RewardFulfillment>().Update(last);
        }
        else
        {
            await _uow.Repository<RewardFulfillment>().AddAsync(new RewardFulfillment
            {
                EventAwardId = awardId,
                AttemptNo = nextAttempt,
                RecipientInfoProtectedJson = last?.RecipientInfoProtectedJson,
                TrackingOrBankTxCode = request.TrackingOrBankTxCode,
                FailureReason = request.FailureReason,
                Status = request.Delivered ? RewardFulfillmentStatus.Delivered : RewardFulfillmentStatus.Failed,
                HandledByUserId = operatorUserId,
                DeliveredAt = request.Delivered ? DateTime.UtcNow : null
            }, ct);
        }

        award.Status = request.Delivered ? EventAwardStatus.Fulfilled : EventAwardStatus.Failed;
        award.UpdatedAt = DateTime.UtcNow;
        _uow.Repository<EventAward>().Update(award);
        await _uow.SaveChangesAsync(ct);

        await _audit.LogAsync(operatorUserId, "EventRewardFulfillmentUpdated", nameof(EventAward), award.Id, null, new { award.Status, request.TrackingOrBankTxCode }, ct);
        var enriched = await EventAwardEnricher.MapForOperatorsAsync(_uow, award.EventId, [award], null, ct);
        return enriched[0];
    }

    private static EventAwardSlotDto MapAward(EventAward a) =>
        EventAwardEnricher.MapBase(a, includeOperatorFields: false);
}
