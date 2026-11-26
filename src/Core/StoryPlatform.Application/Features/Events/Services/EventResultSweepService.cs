using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.Events.Interfaces;
using StoryPlatform.Application.Features.Notifications.DTOs;
using StoryPlatform.Application.Features.Notifications.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.Events.Services;

/// <summary>
/// Tự động chạy các bước sau khi sự kiện theo hoạt động kết thúc:
/// (1) xác minh hồ sơ &amp; lập danh sách tạm,
/// (2) gửi thông báo kết thúc kèm tóm tắt cá nhân một lần,
/// (3) mở nhận thưởng khi hết 48 giờ khiếu nại và không còn đơn mở.
/// </summary>
public sealed class EventResultSweepService : IEventResultSweepService
{
    private readonly IUnitOfWork _uow;
    private readonly IAuditLogWriter _audit;
    private readonly INotificationService? _notifications;
    private readonly ILogger<EventResultSweepService>? _logger;
    private readonly INotificationRealtimePublisher? _notificationPublisher;

    public EventResultSweepService(
        IUnitOfWork uow,
        IAuditLogWriter audit,
        INotificationService? notifications = null,
        ILogger<EventResultSweepService>? logger = null,
        INotificationRealtimePublisher? notificationPublisher = null)
    {
        _uow = uow;
        _audit = audit;
        _notifications = notifications;
        _logger = logger;
        _notificationPublisher = notificationPublisher;
    }

    public async Task<int> SweepEndedEventsAsync(CancellationToken ct = default)
    {
        var candidates = await _uow.Repository<Event>().FindAsync(
            e => e.ConfigVersion == EventConfigVersion.ActivityBased &&
                 (e.Status == EventStatus.Published ||
                  (e.Status == EventStatus.Paused && e.ResultsPublishedAt != null) ||
                  (e.Status == EventStatus.Closed && e.EndedNotifiedAt == null)) &&
                 !e.IsDeleted,
            cancellationToken: ct);
        if (candidates.Count == 0) return 0;

        var processed = 0;
        var participationSvc = new EventParticipationService(_uow, _audit, notifications: _notifications);
        var rewardSvc = new EventRewardService(_uow, _audit, notifications: _notifications);

        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var activities = await _uow.Repository<EventActivity>().FindAsync(
                    a => a.EventId == candidate.Id, cancellationToken: ct);
                if (candidate.Status != EventStatus.Closed &&
                    !EventExecution.HasEnded(candidate, Array.Empty<EventTrack>(), activities, allowPaused: true))
                    continue;

                var didWork = false;

                // Bước 1: Tự xác minh và lập danh sách tạm (1 lần khi còn hồ sơ chờ xác minh).
                if (candidate.Status == EventStatus.Published && !candidate.ResultsFinalizedAt.HasValue &&
                    await HasPendingParticipationsAsync(candidate.Id, ct))
                {
                    if (await TryAutoVerifyAndProposeAsync(candidate.Id, participationSvc, rewardSvc, ct))
                        didWork = true;
                }

                // Bước 2: Thông báo kết thúc sự kiện kèm tóm tắt cá nhân (chỉ gửi một lần).
                if (!candidate.EndedNotifiedAt.HasValue)
                {
                    if (await TryNotifyEndedAsync(candidate.Id, ct))
                        didWork = true;
                }

                // Bước 3: Hết hạn khiếu nại & không còn đơn mở -> mở nhận thưởng cho suất AppealHold.
                if (candidate.Status is (EventStatus.Published or EventStatus.Paused) && candidate.ResultsPublishedAt.HasValue &&
                    DateTime.UtcNow >= candidate.ResultsPublishedAt.Value.AddHours(EventAppealWindow.Hours))
                {
                    if (await TryReleaseAppealHoldAsync(candidate.Id, ct))
                        didWork = true;
                }

                if (didWork) processed++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _uow.DiscardTrackedChanges();
                _logger?.LogWarning("Event result sweep failed for event {EventId}: {ErrorType}",
                    candidate.Id, ex.GetType().Name);
            }
        }

        return processed;
    }

    private async Task<bool> TryAutoVerifyAndProposeAsync(
        int eventId,
        EventParticipationService participationSvc,
        EventRewardService rewardSvc,
        CancellationToken ct)
    {
        await _uow.BeginTransactionAsync(ct);
        try
        {
            await _uow.AcquireEventLockAsync(eventId, ct);
            var ev = await _uow.Repository<Event>().GetByIdAsync(eventId, ct);
            if (ev == null ||
                ev.ConfigVersion != EventConfigVersion.ActivityBased ||
                ev.Status != EventStatus.Published ||
                ev.ResultsFinalizedAt.HasValue)
            {
                await _uow.CommitTransactionAsync(ct);
                return false;
            }

            var activities = await _uow.Repository<EventActivity>().FindAsync(
                a => a.EventId == ev.Id, cancellationToken: ct);
            if (!EventExecution.HasEnded(ev, Array.Empty<EventTrack>(), activities) ||
                !await HasPendingParticipationsAsync(ev.Id, ct))
            {
                await _uow.CommitTransactionAsync(ct);
                return false;
            }

            await participationSvc.VerifyEventParticipationsCoreLockedAsync(actorUserId: null, ev.Id, ct);
            await rewardSvc.RecalculateActivityProposedAwardsInTransactionAsync(ev, activities, ct);
            await _uow.SaveChangesAsync(ct);

            var awards = await _uow.Repository<EventAward>().FindAsync(a => a.EventId == ev.Id, cancellationToken: ct);
            await _audit.LogAsync(null, "EventAwardsProposed", nameof(Event), ev.Id, null,
                new { Count = awards.Count(a => a.Status == EventAwardStatus.AwardProposed && a.ParticipationId.HasValue), Automatic = true }, ct);

            await _uow.CommitTransactionAsync(ct);
            return true;
        }
        catch
        {
            await _uow.RollbackTransactionAsync(CancellationToken.None);
            _uow.DiscardTrackedChanges();
            throw;
        }
    }

    private async Task<bool> TryNotifyEndedAsync(int eventId, CancellationToken ct)
    {
        var notices = new List<(int UserId, NotificationDto Notice)>();

        await _uow.BeginTransactionAsync(ct);
        try
        {
            await _uow.AcquireEventLockAsync(eventId, ct);
            var ev = await _uow.Repository<Event>().GetByIdAsync(eventId, ct);
            if (ev == null ||
                ev.ConfigVersion != EventConfigVersion.ActivityBased ||
                ev.Status is not (EventStatus.Published or EventStatus.Paused or EventStatus.Closed) ||
                ev.EndedNotifiedAt.HasValue)
            {
                await _uow.CommitTransactionAsync(ct);
                return false;
            }

            var activities = await _uow.Repository<EventActivity>().FindAsync(
                a => a.EventId == ev.Id, cancellationToken: ct);
            if (ev.Status != EventStatus.Closed &&
                !EventExecution.HasEnded(ev, Array.Empty<EventTrack>(), activities, allowPaused: true))
            {
                await _uow.CommitTransactionAsync(ct);
                return false;
            }

            if (_notifications != null)
            {
                var participations = await _uow.Repository<EventParticipation>().FindAsync(
                    p => p.EventId == ev.Id && p.EventActivityId != null && !p.IsDeleted, cancellationToken: ct);
                foreach (var uid in participations.Select(p => p.UserId).Distinct())
                {
                    var recap = await EventRecap.BuildAsync(_uow, ev, uid, ct);
                    var payload = JsonSerializer.Serialize(new
                    {
                        eventId,
                        eventTitle = ev.Title,
                        storiesRead = recap.StoriesRead,
                        submissions = recap.Submissions,
                        votes = recap.Votes,
                        tasksCompleted = recap.TasksCompleted,
                        tasksTotal = recap.TasksTotal
                    });
                    notices.Add((uid, await _notifications.CreateStoredAsync(uid, NotificationType.EventEnded, payload, ct)));
                }
            }

            var now = DateTime.UtcNow;
            ev.EndedNotifiedAt = now;
            ev.UpdatedAt = now;
            _uow.Repository<Event>().Update(ev);
            await _uow.SaveChangesAsync(ct);
            await _uow.CommitTransactionAsync(ct);
        }
        catch
        {
            await _uow.RollbackTransactionAsync(CancellationToken.None);
            _uow.DiscardTrackedChanges();
            throw;
        }

        await EventNotificationDelivery.PublishAsync(_notificationPublisher, notices, _logger);

        return true;
    }

    private async Task<bool> TryReleaseAppealHoldAsync(int eventId, CancellationToken ct)
    {
        var notices = new List<(int UserId, NotificationDto Notice)>();

        await _uow.BeginTransactionAsync(ct);
        try
        {
            await _uow.AcquireEventLockAsync(eventId, ct);
            var ev = await _uow.Repository<Event>().GetByIdAsync(eventId, ct);
            var now = DateTime.UtcNow;
            if (ev == null ||
                ev.ConfigVersion != EventConfigVersion.ActivityBased ||
                ev.Status is not (EventStatus.Published or EventStatus.Paused) ||
                !ev.ResultsPublishedAt.HasValue ||
                now < ev.ResultsPublishedAt.Value.AddHours(EventAppealWindow.Hours))
            {
                await _uow.CommitTransactionAsync(ct);
                return false;
            }

            var hasOpenAppeals = await _uow.Repository<EventAppeal>().ExistsAsync(
                a => a.EventId == ev.Id &&
                     (a.Status == EventAppealStatus.Open ||
                      a.Status == EventAppealStatus.UnderReview ||
                      a.Status == EventAppealStatus.Escalated), ct);
            if (hasOpenAppeals)
            {
                await _uow.CommitTransactionAsync(ct);
                return false;
            }

            var heldAwards = await _uow.Repository<EventAward>().FindAsync(
                a => a.EventId == ev.Id && a.Status == EventAwardStatus.AppealHold && a.ParticipationId != null,
                cancellationToken: ct);
            if (heldAwards.Count == 0)
            {
                await _uow.CommitTransactionAsync(ct);
                return false;
            }

            var activities = (await _uow.Repository<EventActivity>().FindAsync(
                a => a.EventId == ev.Id, cancellationToken: ct)).ToDictionary(a => a.Id);
            var partIds = heldAwards.Select(a => a.ParticipationId!.Value).Distinct().ToList();
            var parts = (await _uow.Repository<EventParticipation>().FindAsync(
                p => partIds.Contains(p.Id), cancellationToken: ct)).ToDictionary(p => p.Id);

            foreach (var award in heldAwards)
            {
                var claimHours = award.EventActivityId.HasValue && activities.TryGetValue(award.EventActivityId.Value, out var act)
                    ? act.ClaimWindowHours
                    : 72;
                if (_notifications != null && parts.TryGetValue(award.ParticipationId!.Value, out var part))
                {
                    var payload = JsonSerializer.Serialize(new { eventId, awardId = award.Id, rankOrTier = award.RankOrTier, rewardName = award.RewardName });
                    notices.Add((part.UserId, await _notifications.CreateStoredAsync(part.UserId, NotificationType.EventAwardApproved, payload, ct)));
                }
                award.Status = EventAwardStatus.AwaitingClaim;
                award.ClaimDeadlineAt = now.AddHours(claimHours);
                award.UpdatedAt = now;
                _uow.Repository<EventAward>().Update(award);
            }

            await _uow.SaveChangesAsync(ct);
            await _audit.LogAsync(null, "EventAwardsClaimOpened", nameof(Event), ev.Id, null,
                new { Count = heldAwards.Count }, ct);
            await _uow.CommitTransactionAsync(ct);
        }
        catch
        {
            await _uow.RollbackTransactionAsync(CancellationToken.None);
            _uow.DiscardTrackedChanges();
            throw;
        }

        await EventNotificationDelivery.PublishAsync(_notificationPublisher, notices, _logger);

        return true;
    }

    private Task<bool> HasPendingParticipationsAsync(int eventId, CancellationToken ct) =>
        _uow.Repository<EventParticipation>().ExistsAsync(
            p => p.EventId == eventId &&
                 (p.Status == EventParticipationStatus.Registered ||
                  p.Status == EventParticipationStatus.MissionCompleted ||
                  p.Status == EventParticipationStatus.PendingVerification),
            ct);
}
