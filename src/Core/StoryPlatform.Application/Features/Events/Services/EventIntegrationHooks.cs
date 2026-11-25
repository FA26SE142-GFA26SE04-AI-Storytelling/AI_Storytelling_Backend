using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Common.Time;
using StoryPlatform.Application.Features.Events.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.Events.Services;

/// <summary>
/// Tích hợp hoạt động sự kiện với các nghiệp vụ truyện và đọc.
/// </summary>
public class EventIntegrationHooks : IEventIntegrationHooks
{
    private readonly IUnitOfWork _uow;

    public EventIntegrationHooks(IUnitOfWork uow)
    {
        _uow = uow;
    }

    // Used by event submission while it already holds this event's transaction lock.
    internal async Task LinkApprovedStoryAsync(int eventId, int storyId, int approvedVersionId, CancellationToken ct)
    {
        var submissions = await _uow.Repository<EventSubmission>().FindAsync(
            s => s.EventId == eventId && s.StoryId == storyId && s.CurrentStoryVersionId == approvedVersionId &&
                s.Status == EventSubmissionStatus.PendingReview, cancellationToken: ct);
        foreach (var submission in submissions)
        {
            var campaign = await _uow.Repository<Event>().GetByIdAsync(submission.EventId, ct);
            var now = DateTime.UtcNow;
            if (campaign?.ConfigVersion == EventConfigVersion.ActivityBased)
            {
                var subAct = await _uow.Repository<EventActivity>().FirstOrDefaultAsync(
                    a => a.EventId == campaign.Id && a.ActivityType == EventActivityType.Submission, cancellationToken: ct);
                var voteAct = await _uow.Repository<EventActivity>().FirstOrDefaultAsync(
                    a => a.EventId == campaign.Id && a.ActivityType == EventActivityType.Voting, cancellationToken: ct);
                var cutoff = subAct?.ReviewCutoffAt ?? subAct?.EndAt;
                if (campaign.Status != EventStatus.Published || campaign.ResultsFinalizedAt.HasValue ||
                    (cutoff.HasValue && now > cutoff.Value) || (voteAct != null && now >= voteAct.StartAt))
                    continue;
            }
            else if (campaign?.Format == EventFormat.SharedCreatorParent)
            {
                var tracks = await _uow.Repository<EventTrack>().FindAsync(t => t.EventId == campaign.Id, cancellationToken: ct);
                var cutoff = campaign.ReviewCutoffAt ?? campaign.SubmissionCutoffAt ??
                    tracks.FirstOrDefault(t => t.TrackType == EventTrackType.Creator)?.ExecutionEndAt;
                var votingStart = campaign.VotingStartAt ?? tracks.FirstOrDefault(t => t.TrackType == EventTrackType.Parent)?.ExecutionStartAt;
                if (campaign.Status != EventStatus.Published || campaign.ResultsFinalizedAt.HasValue ||
                    (cutoff.HasValue && now > cutoff.Value) || (votingStart.HasValue && now >= votingStart.Value))
                    continue;
            }
            else if (campaign != null)
            {
                var tracks = await _uow.Repository<EventTrack>().FindAsync(t => t.EventId == campaign.Id, cancellationToken: ct);
                var cutoff = campaign.ReviewCutoffAt ?? campaign.SubmissionCutoffAt ??
                    tracks.FirstOrDefault(t => t.TrackType == EventTrackType.Creator)?.ExecutionEndAt;
                if (campaign.Status != EventStatus.Published || campaign.ResultsFinalizedAt.HasValue ||
                    (cutoff.HasValue && now > cutoff.Value))
                    continue;
            }

            submission.LockedStoryVersionId = approvedVersionId;
            submission.Status = EventSubmissionStatus.Approved;
            submission.UpdatedAt = now;
            _uow.Repository<EventSubmission>().Update(submission);

            var link = await _uow.Repository<EventStory>().FirstOrDefaultAsync(
                es => es.EventId == submission.EventId && es.StoryId == storyId, cancellationToken: ct);
            if (link == null)
            {
                await _uow.Repository<EventStory>().AddAsync(new EventStory
                {
                    EventId = submission.EventId,
                    StoryId = storyId,
                    StoryVersionId = approvedVersionId,
                    Source = EventStorySource.CreatorSubmission,
                    EventSubmissionId = submission.Id,
                    IsActive = true
                }, ct);
            }
            else
            {
                link.StoryVersionId = approvedVersionId;
                link.EventSubmissionId = submission.Id;
                link.IsActive = true;
                _uow.Repository<EventStory>().Update(link);
            }
        }

        await _uow.SaveChangesAsync(ct);
    }

    public async Task EnsureStoryNotLockedInEventAsync(int storyId, CancellationToken ct = default)
    {
        var links = await _uow.Repository<EventStory>().FindAsync(es => es.StoryId == storyId && es.IsActive, cancellationToken: ct);
        if (links.Count == 0) return;

        var eventIds = links.Select(l => l.EventId).Distinct().ToList();
        var activeEvent = await _uow.Repository<Event>().ExistsAsync(
            e => eventIds.Contains(e.Id) && e.Status != EventStatus.Closed && e.Status != EventStatus.Archived, ct);
        if (activeEvent)
            throw new ConflictException("Truyện đang phục vụ sự kiện chưa kết thúc; không thể gửi yêu cầu gỡ lúc này.");
    }

    public async Task<bool> IsStoryExemptFromWeeklyLimitAsync(int ownerUserId, int storyId, CancellationToken ct = default)
    {
        var eventStories = await _uow.Repository<EventStory>().FindAsync(es => es.StoryId == storyId && es.IsActive &&
            es.Source != EventStorySource.ParentSubmission, cancellationToken: ct);
        if (eventStories.Count == 0) return false;

        var eventIds = eventStories.Select(es => es.EventId).Distinct().ToList();
        var now = DateTime.UtcNow;
        var openEvents = await _uow.Repository<Event>().FindAsync(
            e => eventIds.Contains(e.Id) && e.Status == EventStatus.Published, cancellationToken: ct);
        eventIds = openEvents.Select(e => e.Id).ToList();
        var tracks = await _uow.Repository<EventTrack>().FindAsync(
            t => eventIds.Contains(t.EventId) && t.TrackType == EventTrackType.Parent && t.WaiveWeeklyLibraryLimit
                && t.ExecutionStartAt <= now && t.ExecutionEndAt >= now, cancellationToken: ct);
        var trackIds = tracks.Select(t => t.Id).ToHashSet();
        if (trackIds.Count > 0 && await _uow.Repository<EventParticipation>().ExistsAsync(
                p => p.UserId == ownerUserId && p.TrackId.HasValue && trackIds.Contains(p.TrackId.Value) && p.Status != EventParticipationStatus.Disqualified, ct))
            return true;

        var activities = await _uow.Repository<EventActivity>().FindAsync(
            a => eventIds.Contains(a.EventId) && a.ActivityType == EventActivityType.ReadingMissions &&
                 a.WaiveWeeklyLibraryLimit && a.StartAt <= now && a.EndAt >= now, cancellationToken: ct);
        var activityIds = activities.Select(a => a.Id).ToHashSet();
        return activityIds.Count > 0 && await _uow.Repository<EventParticipation>().ExistsAsync(
            p => p.UserId == ownerUserId && p.EventActivityId.HasValue && activityIds.Contains(p.EventActivityId.Value) && p.Status != EventParticipationStatus.Disqualified, ct);
    }

    public async Task RecordActivityForActiveEventsAsync(
        int ownerUserId,
        int? childProfileId,
        int? storyId,
        TaskVerifierType verifierType,
        int? readingSessionId = null,
        int? storyRatingId = null,
        CancellationToken ct = default)
    {
        var participations = await _uow.Repository<EventParticipation>().FindAsync(
            p => p.UserId == ownerUserId && !p.IsDeleted && p.Status != EventParticipationStatus.Disqualified,
            cancellationToken: ct);
        if (participations.Count == 0) return;

        var ownsTransaction = !_uow.HasActiveTransaction;
        if (ownsTransaction) await _uow.BeginTransactionAsync(ct);

        try
        {
            foreach (var eventId in participations.Select(p => p.EventId).Distinct().OrderBy(id => id))
            {
                await _uow.AcquireEventLockAsync(eventId, ct);
            }

            var now = DateTime.UtcNow;
            var localDate = DateOnly.FromDateTime(VietnamTime.LocalDate(now));
            var recordedAny = false;

            foreach (var p in participations)
            {
                var ev = await _uow.Repository<Event>().GetByIdAsync(p.EventId, ct);
                if (ev == null || ev.Status != EventStatus.Published) continue;

                if (ev.ConfigVersion == EventConfigVersion.ActivityBased)
                {
                    if (!p.EventActivityId.HasValue) continue;
                    var act = await _uow.Repository<EventActivity>().GetByIdAsync(p.EventActivityId.Value, ct);
                    if (act == null || act.ActivityType != EventActivityType.ReadingMissions ||
                        now < act.StartAt || now > act.EndAt) continue;
                }
                else
                {
                    if (!p.TrackId.HasValue) continue;
                    var track = await _uow.Repository<EventTrack>().GetByIdAsync(p.TrackId.Value, ct);
                    if (track == null || track.TrackType != EventTrackType.Parent ||
                        now < track.ExecutionStartAt || now > track.ExecutionEndAt) continue;
                }

                if (storyId.HasValue)
                {
                    var inEvent = await _uow.Repository<EventStory>().ExistsAsync(
                        es => es.EventId == ev.Id && es.StoryId == storyId.Value && es.IsActive, ct);
                    if (!inEvent) continue;
                }

                var tasks = await _uow.Repository<EventTaskDefinition>().FindAsync(
                    t => t.EventRuleVersionId == p.AcceptedRuleVersionId && t.VerifierType == verifierType, cancellationToken: ct);
                if (tasks.Count == 0) continue;

                foreach (var task in tasks)
                {
                    if (readingSessionId.HasValue && await _uow.Repository<EventTaskProgress>().ExistsAsync(
                            pr => pr.ParticipationId == p.Id && pr.TaskDefinitionId == task.Id && pr.ReadingSessionId == readingSessionId.Value, ct))
                        continue;
                    if (storyRatingId.HasValue && await _uow.Repository<EventTaskProgress>().ExistsAsync(
                            pr => pr.ParticipationId == p.Id && pr.TaskDefinitionId == task.Id && pr.StoryRatingId == storyRatingId.Value, ct))
                        continue;

                    await _uow.Repository<EventTaskProgress>().AddAsync(new EventTaskProgress
                    {
                        ParticipationId = p.Id,
                        TaskDefinitionId = task.Id,
                        ChildProfileId = childProfileId,
                        StoryId = storyId,
                        ReadingSessionId = readingSessionId,
                        StoryRatingId = storyRatingId,
                        ActivityLocalDate = localDate,
                        CompletedAt = now
                    }, ct);
                    recordedAny = true;
                }
            }

            if (recordedAny) await _uow.SaveChangesAsync(ct);
            if (ownsTransaction) await _uow.CommitTransactionAsync(ct);
        }
        catch
        {
            if (ownsTransaction)
            {
                await _uow.RollbackTransactionAsync(CancellationToken.None);
                _uow.DiscardTrackedChanges();
            }
            throw;
        }
    }
}
