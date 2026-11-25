using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.Events.Services;
using StoryPlatform.Application.Features.Moderation.DTOs;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.Moderation;

internal sealed record ActiveEventSubmissionLink(
    Event Event,
    EventSubmission Submission,
    DateTime? ReviewCutoffAt,
    HashSet<int> AssignedModeratorIds,
    bool ReviewExpired = false);

internal sealed record ActiveVersionEventInfo(
    List<ModerationItemEventSubmissionDto> Events,
    DateTime? NearestReviewCutoffAt,
    IReadOnlyList<ActiveEventSubmissionLink> Links)
{
    public bool CanModeratorReview(int moderatorUserId) =>
        Links.All(link => link.AssignedModeratorIds.Contains(moderatorUserId));
}

internal sealed record EventSubmissionAccessCheck(
    bool IsAllowed,
    string? DenialReason,
    IReadOnlyList<ActiveEventSubmissionLink> ActiveLinks);

internal static class EventSubmissionModerationAccess
{
    public static async Task<Dictionary<(int StoryId, int StoryVersionId), ActiveVersionEventInfo>> LoadActiveByVersionAsync(
        IUnitOfWork uow, DateTime now, CancellationToken ct = default)
    {
        var submissionRepo = uow.Repository<EventSubmission>();
        var eventRepo = uow.Repository<Event>();
        if (submissionRepo == null || eventRepo == null)
        {
            return new();
        }

        var pendingSubmissions = await submissionRepo.FindAsync(
            s => !s.IsDeleted
                 && s.ParentSubmissionText == null
                 && s.Status == EventSubmissionStatus.PendingReview,
            cancellationToken: ct);
        if (pendingSubmissions.Count == 0)
        {
            return new();
        }

        var eventIds = pendingSubmissions.Select(s => s.EventId).Distinct().ToList();
        var events = (await eventRepo.FindAsync(
                e => eventIds.Contains(e.Id)
                     && !e.IsDeleted
                     && e.Status == EventStatus.Published
                     && e.ResultsFinalizedAt == null,
                cancellationToken: ct))
            .ToDictionary(e => e.Id);
        if (events.Count == 0)
        {
            return new();
        }

        var activeEventIds = events.Keys.ToList();
        var activityRepo = uow.Repository<EventActivity>();
        var trackRepo = uow.Repository<EventTrack>();
        var assignmentRepo = uow.Repository<EventModeratorAssignment>();

        var activities = activityRepo == null
            ? Array.Empty<EventActivity>()
            : await activityRepo.FindAsync(
                a => activeEventIds.Contains(a.EventId) && !a.IsDeleted,
                cancellationToken: ct);
        var tracks = trackRepo == null
            ? Array.Empty<EventTrack>()
            : await trackRepo.FindAsync(
                t => activeEventIds.Contains(t.EventId) && !t.IsDeleted,
                cancellationToken: ct);
        var assignments = assignmentRepo == null
            ? Array.Empty<EventModeratorAssignment>()
            : await assignmentRepo.FindAsync(
                a => activeEventIds.Contains(a.EventId) && !a.IsDeleted,
                cancellationToken: ct);

        var activitiesByEvent = activities.GroupBy(a => a.EventId).ToDictionary(g => g.Key, g => g.ToList());
        var tracksByEvent = tracks.GroupBy(t => t.EventId).ToDictionary(g => g.Key, g => g.ToList());
        var assistantsByEvent = assignments
            .GroupBy(a => a.EventId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.ModeratorId).ToHashSet());

        var eventStates = new Dictionary<int, (Event Event, DateTime? Cutoff, HashSet<int> Moderators)>();
        foreach (var ev in events.Values)
        {
            activitiesByEvent.TryGetValue(ev.Id, out var evActivities);
            tracksByEvent.TryGetValue(ev.Id, out var evTracks);
            if (!TryEvaluateActiveReviewWindow(ev, evActivities, evTracks, now, out var cutoff))
            {
                continue;
            }

            var moderators = new HashSet<int>();
            var ownerId = EventModeratorAccess.OwnerId(ev);
            if (ownerId > 0)
            {
                moderators.Add(ownerId);
            }
            if (assistantsByEvent.TryGetValue(ev.Id, out var assistants))
            {
                foreach (var assistantId in assistants)
                {
                    moderators.Add(assistantId);
                }
            }

            eventStates[ev.Id] = (ev, cutoff, moderators);
        }

        if (eventStates.Count == 0)
        {
            return new();
        }

        var grouped = new Dictionary<(int StoryId, int StoryVersionId), List<ActiveEventSubmissionLink>>();
        foreach (var sub in pendingSubmissions.OrderBy(s => s.EventId).ThenByDescending(s => s.Id))
        {
            if (!eventStates.TryGetValue(sub.EventId, out var state))
            {
                continue;
            }

            var key = (sub.StoryId, sub.CurrentStoryVersionId);
            if (!grouped.TryGetValue(key, out var list))
            {
                list = new List<ActiveEventSubmissionLink>();
                grouped[key] = list;
            }

            // Phòng trường hợp dữ liệu cũ có nhiều dòng cho cùng sự kiện: ưu tiên dòng mới nhất.
            if (list.Any(existing => existing.Event.Id == sub.EventId))
            {
                continue;
            }

            list.Add(new ActiveEventSubmissionLink(state.Event, sub, state.Cutoff, state.Moderators));
        }

        var result = new Dictionary<(int StoryId, int StoryVersionId), ActiveVersionEventInfo>(grouped.Count);
        foreach (var (key, links) in grouped)
        {
            var orderedLinks = links
                .OrderBy(l => l.ReviewCutoffAt ?? DateTime.MaxValue)
                .ThenBy(l => l.Event.Id)
                .ToList();
            var dtos = orderedLinks
                .Select(l => new ModerationItemEventSubmissionDto
                {
                    EventId = l.Event.Id,
                    EventTitle = l.Event.Title,
                    ReviewCutoffAt = l.ReviewCutoffAt,
                    EventSubmissionId = l.Submission.Id
                })
                .ToList();
            var nearest = orderedLinks
                .Where(l => l.ReviewCutoffAt.HasValue)
                .Select(l => l.ReviewCutoffAt!.Value)
                .DefaultIfEmpty()
                .Min();
            DateTime? nearestCutoff = orderedLinks.Any(l => l.ReviewCutoffAt.HasValue) ? nearest : null;

            result[key] = new ActiveVersionEventInfo(dtos, nearestCutoff, orderedLinks);
        }

        return result;
    }

    public static async Task<IReadOnlyList<ActiveEventSubmissionLink>> LoadActiveLinksForVersionAsync(
        IUnitOfWork uow, int storyId, int storyVersionId, DateTime now, CancellationToken ct = default,
        bool includeExpired = false)
    {
        var submissionRepo = uow.Repository<EventSubmission>();
        var eventRepo = uow.Repository<Event>();
        if (submissionRepo == null || eventRepo == null)
        {
            return Array.Empty<ActiveEventSubmissionLink>();
        }

        var submissions = await submissionRepo.FindAsync(
            s => !s.IsDeleted
                 && s.ParentSubmissionText == null
                 && s.StoryId == storyId
                 && s.CurrentStoryVersionId == storyVersionId
                 && s.Status == EventSubmissionStatus.PendingReview,
            cancellationToken: ct);
        if (submissions.Count == 0)
        {
            return Array.Empty<ActiveEventSubmissionLink>();
        }

        var eventIds = submissions.Select(s => s.EventId).Distinct().OrderBy(id => id).ToList();
        var events = (await eventRepo.FindAsync(
                e => eventIds.Contains(e.Id)
                     && !e.IsDeleted
                     && ((e.Status == EventStatus.Published && e.ResultsFinalizedAt == null)
                         || (includeExpired && (e.Status == EventStatus.Published || e.Status == EventStatus.Closed))),
                cancellationToken: ct))
            .ToDictionary(e => e.Id);
        if (events.Count == 0)
        {
            return Array.Empty<ActiveEventSubmissionLink>();
        }

        var activeIds = events.Keys.ToList();
        var activityRepo = uow.Repository<EventActivity>();
        var trackRepo = uow.Repository<EventTrack>();
        var assignmentRepo = uow.Repository<EventModeratorAssignment>();

        var activities = activityRepo == null
            ? Array.Empty<EventActivity>()
            : await activityRepo.FindAsync(a => activeIds.Contains(a.EventId) && !a.IsDeleted, cancellationToken: ct);
        var tracks = trackRepo == null
            ? Array.Empty<EventTrack>()
            : await trackRepo.FindAsync(t => activeIds.Contains(t.EventId) && !t.IsDeleted, cancellationToken: ct);
        var assignments = assignmentRepo == null
            ? Array.Empty<EventModeratorAssignment>()
            : await assignmentRepo.FindAsync(a => activeIds.Contains(a.EventId) && !a.IsDeleted, cancellationToken: ct);

        var links = new List<ActiveEventSubmissionLink>();
        foreach (var sub in submissions.OrderBy(s => s.EventId).ThenByDescending(s => s.Id))
        {
            if (!events.TryGetValue(sub.EventId, out var ev))
            {
                continue;
            }

            if (links.Any(l => l.Event.Id == sub.EventId))
            {
                continue;
            }

            var evActivities = activities.Where(a => a.EventId == ev.Id).ToList();
            var evTracks = tracks.Where(t => t.EventId == ev.Id).ToList();
            if (!TryEvaluateActiveReviewWindow(ev, evActivities, evTracks, now, out var cutoff))
            {
                if (includeExpired)
                    links.Add(new ActiveEventSubmissionLink(ev, sub, cutoff, [], ReviewExpired: true));
                continue;
            }

            var moderators = new HashSet<int>();
            var ownerId = EventModeratorAccess.OwnerId(ev);
            if (ownerId > 0)
            {
                moderators.Add(ownerId);
            }
            foreach (var assignment in assignments.Where(a => a.EventId == ev.Id))
            {
                moderators.Add(assignment.ModeratorId);
            }

            links.Add(new ActiveEventSubmissionLink(ev, sub, cutoff, moderators));
        }

        return links
            .OrderBy(l => l.ReviewCutoffAt ?? DateTime.MaxValue)
            .ThenBy(l => l.Event.Id)
            .ToList();
    }

    public static async Task<List<ModerationItemEventSubmissionDto>> LoadLinkedForItemAsync(
        IUnitOfWork uow, ModerationItem item, DateTime now, CancellationToken ct = default)
    {
        if (item.ItemType != ModerationItemType.PublicStoryReview
            || !item.StoryId.HasValue
            || !item.StoryVersionId.HasValue)
        {
            return new();
        }

        if (item.Status is ModerationItemStatus.Pending or ModerationItemStatus.Claimed)
        {
            var active = await LoadActiveLinksForVersionAsync(
                uow, item.StoryId.Value, item.StoryVersionId.Value, now, ct);
            return active.Select(ToDto).ToList();
        }

        var map = await LoadHistoricalMapForItemsAsync(uow, [item], ct);
        return map.TryGetValue((item.StoryId.Value, item.StoryVersionId.Value), out var list)
            ? list
            : new();
    }

    public static async Task<Dictionary<(int StoryId, int StoryVersionId), List<ModerationItemEventSubmissionDto>>>
        LoadHistoricalMapForItemsAsync(
            IUnitOfWork uow, IReadOnlyList<ModerationItem> items, CancellationToken ct = default)
    {
        var submissionRepo = uow.Repository<EventSubmission>();
        var eventRepo = uow.Repository<Event>();
        if (submissionRepo == null || eventRepo == null)
        {
            return new();
        }

        var versionPairs = items
            .Where(i => i.ItemType == ModerationItemType.PublicStoryReview
                        && i.StoryId.HasValue
                        && i.StoryVersionId.HasValue)
            .Select(i => (StoryId: i.StoryId!.Value, VersionId: i.StoryVersionId!.Value))
            .Distinct()
            .ToList();
        if (versionPairs.Count == 0)
        {
            return new();
        }

        var storyIds = versionPairs.Select(p => p.StoryId).Distinct().ToList();
        var versionIds = versionPairs.Select(p => p.VersionId).Distinct().ToList();
        var submissions = await submissionRepo.FindAsync(
            s => !s.IsDeleted
                 && s.ParentSubmissionText == null
                 && storyIds.Contains(s.StoryId)
                 && (versionIds.Contains(s.CurrentStoryVersionId)
                     || (s.LockedStoryVersionId.HasValue && versionIds.Contains(s.LockedStoryVersionId.Value))),
            cancellationToken: ct);
        if (submissions.Count == 0)
        {
            return new();
        }

        var eventIds = submissions.Select(s => s.EventId).Distinct().ToList();
        var events = (await eventRepo.FindAsync(
                e => eventIds.Contains(e.Id) && !e.IsDeleted,
                cancellationToken: ct))
            .ToDictionary(e => e.Id);
        if (events.Count == 0)
        {
            return new();
        }

        var activityRepo = uow.Repository<EventActivity>();
        var trackRepo = uow.Repository<EventTrack>();
        var activities = activityRepo == null
            ? Array.Empty<EventActivity>()
            : await activityRepo.FindAsync(a => eventIds.Contains(a.EventId) && !a.IsDeleted, cancellationToken: ct);
        var tracks = trackRepo == null
            ? Array.Empty<EventTrack>()
            : await trackRepo.FindAsync(t => eventIds.Contains(t.EventId) && !t.IsDeleted, cancellationToken: ct);

        var result = new Dictionary<(int StoryId, int StoryVersionId), List<ModerationItemEventSubmissionDto>>();
        foreach (var pair in versionPairs)
        {
            var matched = submissions
                .Where(s => s.StoryId == pair.StoryId
                            && (s.CurrentStoryVersionId == pair.VersionId || s.LockedStoryVersionId == pair.VersionId)
                            && events.ContainsKey(s.EventId))
                .GroupBy(s => s.EventId)
                .Select(g => g.OrderByDescending(x => x.Id).First())
                .Select(s =>
                {
                    var ev = events[s.EventId];
                    var cutoff = ResolveDisplayReviewCutoff(
                        ev,
                        activities.Where(a => a.EventId == ev.Id).ToList(),
                        tracks.Where(t => t.EventId == ev.Id).ToList());
                    return new ModerationItemEventSubmissionDto
                    {
                        EventId = ev.Id,
                        EventTitle = ev.Title,
                        ReviewCutoffAt = cutoff,
                        EventSubmissionId = s.Id
                    };
                })
                .OrderBy(d => d.ReviewCutoffAt ?? DateTime.MaxValue)
                .ThenBy(d => d.EventId)
                .ToList();

            if (matched.Count > 0)
            {
                result[pair] = matched;
            }
        }

        return result;
    }

    public static async Task<HashSet<int>> LoadAssignedEventIdsAsync(
        IUnitOfWork uow, int moderatorUserId, CancellationToken ct = default)
    {
        var eventRepo = uow.Repository<Event>();
        if (eventRepo == null)
        {
            return new();
        }

        var owned = await eventRepo.FindAsync(
            e => !e.IsDeleted
                 && (e.AssignedModeratorId == moderatorUserId
                     || (e.AssignedModeratorId <= 0 && e.CreatedByUserId == moderatorUserId)),
            cancellationToken: ct);
        var ids = owned.Select(e => e.Id).ToHashSet();

        var assignmentRepo = uow.Repository<EventModeratorAssignment>();
        if (assignmentRepo != null)
        {
            var assisted = await assignmentRepo.FindAsync(
                a => a.ModeratorId == moderatorUserId && !a.IsDeleted,
                cancellationToken: ct);
            foreach (var assignment in assisted)
            {
                ids.Add(assignment.EventId);
            }
        }

        return ids;
    }

    public static async Task<EventSubmissionAccessCheck> CheckActorAccessAsync(
        IUnitOfWork uow, UserAccount actor, ModerationItem item, DateTime now, CancellationToken ct = default)
    {
        if (item.ItemType != ModerationItemType.PublicStoryReview
            || !item.StoryId.HasValue
            || !item.StoryVersionId.HasValue)
        {
            return new(true, null, Array.Empty<ActiveEventSubmissionLink>());
        }

        var activeLinks = await LoadActiveLinksForVersionAsync(
            uow, item.StoryId.Value, item.StoryVersionId.Value, now, ct);
        if (activeLinks.Count == 0 || actor.Role == UserRole.Administrator)
        {
            return new(true, null, activeLinks);
        }

        if (actor.Role != UserRole.Moderator)
        {
            return new(false, "Bạn không có quyền duyệt bài dự thi này.", activeLinks);
        }

        var missing = activeLinks.Where(l => !l.AssignedModeratorIds.Contains(actor.Id)).ToList();
        if (missing.Count == 0)
        {
            return new(true, null, activeLinks);
        }

        var missingNames = string.Join(", ", missing.Select(m => m.Event.Title));
        var message = activeLinks.Count == 1
            ? $"Bạn chưa được phân công phụ trách sự kiện \"{missingNames}\" nên không thể nhận hoặc duyệt bài dự thi này."
            : $"Phiên bản này đang dự thi nhiều sự kiện; bạn chưa được phân công ở tất cả các sự kiện liên quan (thiếu: {missingNames}).";
        return new(false, message, activeLinks);
    }

    public static async Task<IReadOnlyList<ActiveEventSubmissionLink>> AcquireEventAndStoryLocksAsync(
        IUnitOfWork uow, int storyId, int storyVersionId, DateTime now, CancellationToken ct = default,
        bool includeExpired = false)
    {
        var links = await LoadActiveLinksForVersionAsync(uow, storyId, storyVersionId, now, ct, includeExpired);
        var lockedEventIds = new HashSet<int>();
        foreach (var eventId in links.Select(l => l.Event.Id).Distinct().OrderBy(id => id))
        {
            await uow.AcquireEventLockAsync(eventId, ct);
            lockedEventIds.Add(eventId);
        }

        await uow.AcquireTransactionLockAsync(storyId, ct);

        var confirmed = await LoadActiveLinksForVersionAsync(uow, storyId, storyVersionId, now, ct, includeExpired);
        if (confirmed.Any(l => !lockedEventIds.Contains(l.Event.Id)))
        {
            throw new ConflictException("Bài dự thi vừa được gửi thêm vào sự kiện khác; hãy tải lại và thử lại.");
        }

        return confirmed;
    }

    public static async Task ReleaseClaimedItemToPendingAsync(
        IUnitOfWork uow,
        IAuditLogWriter audit,
        int actorUserId,
        ModerationItem trackedItem,
        string reason,
        CancellationToken ct = default)
    {
        if (trackedItem.Status != ModerationItemStatus.Claimed)
        {
            return;
        }

        var previousClaimerId = trackedItem.ClaimedByUserId;
        trackedItem.Status = ModerationItemStatus.Pending;
        trackedItem.ClaimedByUserId = null;
        trackedItem.ClaimedAt = null;
        trackedItem.RowVersion += 1;
        uow.Repository<ModerationItem>().Update(trackedItem);
        await uow.SaveChangesAsync(ct);

        await audit.LogAsync(
            actorUserId,
            "ModerationItemReleasedAssignmentChanged",
            nameof(ModerationItem),
            trackedItem.Id,
            beforeState: new { status = ModerationItemStatus.Claimed.ToString(), claimedByUserId = previousClaimerId },
            afterState: new { status = ModerationItemStatus.Pending.ToString(), reason },
            cancellationToken: ct);
    }

    /// <summary>
    /// Khi Admin cập nhật phân công sự kiện, thu hồi ngay mọi mục đang giữ thuộc sự kiện đó nếu người giữ mất quyền.
    /// </summary>
    public static async Task ReleaseUnauthorizedClaimedItemsForEventAsync(
        IUnitOfWork uow, IAuditLogWriter audit, int actorUserId, int eventId, DateTime now, CancellationToken ct = default)
    {
        var submissionRepo = uow.Repository<EventSubmission>();
        var itemRepo = uow.Repository<ModerationItem>();
        var userRepo = uow.Repository<UserAccount>();
        if (submissionRepo == null || itemRepo == null || userRepo == null)
        {
            return;
        }

        var pendingSubmissions = await submissionRepo.FindAsync(
            s => !s.IsDeleted
                 && s.EventId == eventId
                 && s.ParentSubmissionText == null
                 && s.Status == EventSubmissionStatus.PendingReview,
            cancellationToken: ct);
        if (pendingSubmissions.Count == 0)
        {
            return;
        }

        var storyIds = pendingSubmissions.Select(s => s.StoryId).Distinct().ToList();
        var versionIds = pendingSubmissions.Select(s => s.CurrentStoryVersionId).Distinct().ToList();
        var claimedItems = await itemRepo.FindAsync(
            m => !m.IsDeleted
                 && m.ItemType == ModerationItemType.PublicStoryReview
                 && m.Status == ModerationItemStatus.Claimed
                 && m.StoryId.HasValue && storyIds.Contains(m.StoryId.Value)
                 && m.StoryVersionId.HasValue && versionIds.Contains(m.StoryVersionId.Value),
            cancellationToken: ct);

        foreach (var candidate in claimedItems)
        {
            var matchesSubmission = pendingSubmissions.Any(s =>
                s.StoryId == candidate.StoryId!.Value && s.CurrentStoryVersionId == candidate.StoryVersionId!.Value);
            if (!matchesSubmission || !candidate.ClaimedByUserId.HasValue)
            {
                continue;
            }

            var claimer = await userRepo.GetByIdAsync(candidate.ClaimedByUserId.Value, ct);
            if (claimer == null || claimer.Role == UserRole.Administrator)
            {
                continue;
            }

            var access = await CheckActorAccessAsync(uow, claimer, candidate, now, ct);
            if (access.IsAllowed)
            {
                continue;
            }

            var tracked = await itemRepo.GetByIdAsync(candidate.Id, ct);
            if (tracked is { Status: ModerationItemStatus.Claimed })
            {
                await ReleaseClaimedItemToPendingAsync(
                    uow, audit, actorUserId, tracked,
                    access.DenialReason ?? "Phân công sự kiện thay đổi nên mục được trả về Chờ duyệt.",
                    ct);
            }
        }
    }

    /// <summary>
    /// Khi một phiên bản đang được Moderator giữ lại được nộp thêm vào sự kiện khác, thu hồi mục nếu người đang giữ không đủ quyền ở mọi sự kiện.
    /// </summary>
    public static async Task ReleaseUnauthorizedClaimedItemForVersionAsync(
        IUnitOfWork uow, IAuditLogWriter audit, int actorUserId, int storyId, int storyVersionId, DateTime now, CancellationToken ct = default)
    {
        var itemRepo = uow.Repository<ModerationItem>();
        var userRepo = uow.Repository<UserAccount>();
        if (itemRepo == null || userRepo == null)
        {
            return;
        }

        var claimedItems = await itemRepo.FindAsync(
            m => !m.IsDeleted
                 && m.ItemType == ModerationItemType.PublicStoryReview
                 && m.Status == ModerationItemStatus.Claimed
                 && m.StoryId == storyId
                 && m.StoryVersionId == storyVersionId,
            cancellationToken: ct);

        foreach (var candidate in claimedItems)
        {
            if (!candidate.ClaimedByUserId.HasValue)
            {
                continue;
            }

            var claimer = await userRepo.GetByIdAsync(candidate.ClaimedByUserId.Value, ct);
            if (claimer == null || claimer.Role == UserRole.Administrator)
            {
                continue;
            }

            var access = await CheckActorAccessAsync(uow, claimer, candidate, now, ct);
            if (access.IsAllowed)
            {
                continue;
            }

            var tracked = await itemRepo.GetByIdAsync(candidate.Id, ct);
            if (tracked is { Status: ModerationItemStatus.Claimed })
            {
                await ReleaseClaimedItemToPendingAsync(
                    uow, audit, actorUserId, tracked,
                    access.DenialReason ?? "Phiên bản được nộp thêm vào sự kiện khác mà người đang giữ chưa được phân công.",
                    ct);
            }
        }
    }

    public static async Task ApplyPublicReviewDecisionInTransactionAsync(
        IUnitOfWork uow,
        int storyId,
        int storyVersionId,
        int moderationItemId,
        ModerationItemStatus decision,
        DateTime now,
        CancellationToken ct = default)
    {
        if (decision is not (ModerationItemStatus.Approved or ModerationItemStatus.ChangesRequested or ModerationItemStatus.Rejected))
        {
            return;
        }

        var submissionRepo = uow.Repository<EventSubmission>();
        var eventRepo = uow.Repository<Event>();
        if (submissionRepo == null || eventRepo == null)
        {
            return;
        }

        var activeLinks = await LoadActiveLinksForVersionAsync(uow, storyId, storyVersionId, now, ct, includeExpired: true);
        if (activeLinks.Count == 0)
        {
            return;
        }

        var eventStoryRepo = uow.Repository<EventStory>();
        var changed = false;

        foreach (var link in activeLinks)
        {
            var submission = await submissionRepo.GetByIdAsync(link.Submission.Id, ct);
            if (submission == null || submission.IsDeleted || submission.Status != EventSubmissionStatus.PendingReview)
            {
                continue;
            }

            submission.LatestModerationItemId = moderationItemId;
            submission.UpdatedAt = now;

            if (link.ReviewExpired)
            {
                submission.Status = EventSubmissionStatus.ReviewExpired;
                submissionRepo.Update(submission);
                changed = true;
            }
            else if (decision == ModerationItemStatus.Approved)
            {
                submission.LockedStoryVersionId = storyVersionId;
                submission.Status = EventSubmissionStatus.Approved;
                submissionRepo.Update(submission);
                changed = true;

                if (eventStoryRepo != null)
                {
                    var existingStory = await eventStoryRepo.FirstOrDefaultAsync(
                        es => es.EventId == submission.EventId && es.StoryId == storyId,
                        cancellationToken: ct);
                    if (existingStory == null)
                    {
                        await eventStoryRepo.AddAsync(new EventStory
                        {
                            EventId = submission.EventId,
                            StoryId = storyId,
                            StoryVersionId = storyVersionId,
                            Source = EventStorySource.CreatorSubmission,
                            EventSubmissionId = submission.Id,
                            IsActive = true
                        }, ct);
                    }
                    else
                    {
                        var trackedStory = await eventStoryRepo.GetByIdAsync(existingStory.Id, ct);
                        if (trackedStory != null)
                        {
                            trackedStory.StoryVersionId = storyVersionId;
                            trackedStory.Source = EventStorySource.CreatorSubmission;
                            trackedStory.EventSubmissionId = submission.Id;
                            trackedStory.IsActive = true;
                            trackedStory.UpdatedAt = now;
                            eventStoryRepo.Update(trackedStory);
                        }
                    }
                }
            }
            else if (decision == ModerationItemStatus.ChangesRequested)
            {
                submission.Status = EventSubmissionStatus.ChangesRequested;
                submissionRepo.Update(submission);
                changed = true;
            }
            else
            {
                submission.Status = EventSubmissionStatus.Rejected;
                submissionRepo.Update(submission);
                changed = true;
            }
        }

        if (changed)
        {
            await uow.SaveChangesAsync(ct);
        }
    }

    internal static bool TryEvaluateActiveReviewWindow(
        Event ev,
        IReadOnlyList<EventActivity>? activities,
        IReadOnlyList<EventTrack>? tracks,
        DateTime now,
        out DateTime? displayCutoff)
    {
        displayCutoff = null;
        if (ev.IsDeleted || ev.Status != EventStatus.Published || ev.ResultsFinalizedAt.HasValue)
        {
            return false;
        }

        if (ev.ConfigVersion == EventConfigVersion.ActivityBased)
        {
            var subAct = activities?.FirstOrDefault(a => !a.IsDeleted && a.ActivityType == EventActivityType.Submission);
            if (subAct == null)
            {
                return false;
            }

            var voteAct = activities?.FirstOrDefault(a => !a.IsDeleted && a.ActivityType == EventActivityType.Voting);
            var cutoff = subAct.ReviewCutoffAt ?? subAct.EndAt;
            if (now > cutoff || (voteAct != null && now >= voteAct.StartAt))
            {
                return false;
            }

            displayCutoff = voteAct != null && voteAct.StartAt < cutoff ? voteAct.StartAt : cutoff;
            return true;
        }

        if (ev.Format == EventFormat.SharedCreatorParent)
        {
            var creatorTrack = tracks?.FirstOrDefault(t => !t.IsDeleted && t.TrackType == EventTrackType.Creator);
            var parentTrack = tracks?.FirstOrDefault(t => !t.IsDeleted && t.TrackType == EventTrackType.Parent);
            var cutoff = ev.ReviewCutoffAt ?? ev.SubmissionCutoffAt ?? creatorTrack?.ExecutionEndAt;
            var votingStart = ev.VotingStartAt ?? parentTrack?.ExecutionStartAt;
            if ((cutoff.HasValue && now > cutoff.Value) || (votingStart.HasValue && now >= votingStart.Value))
            {
                return false;
            }

            displayCutoff = votingStart.HasValue && (!cutoff.HasValue || votingStart.Value < cutoff.Value)
                ? votingStart
                : cutoff;
            return true;
        }

        var standardCreatorTrack = tracks?.FirstOrDefault(t => !t.IsDeleted && t.TrackType == EventTrackType.Creator);
        var standardCutoff = ev.ReviewCutoffAt ?? ev.SubmissionCutoffAt ?? standardCreatorTrack?.ExecutionEndAt;
        if (standardCutoff.HasValue && now > standardCutoff.Value)
        {
            return false;
        }

        displayCutoff = standardCutoff;
        return true;
    }

    internal static DateTime? ResolveDisplayReviewCutoff(
        Event ev,
        IReadOnlyList<EventActivity>? activities,
        IReadOnlyList<EventTrack>? tracks)
    {
        if (ev.ConfigVersion == EventConfigVersion.ActivityBased)
        {
            var subAct = activities?.FirstOrDefault(a => !a.IsDeleted && a.ActivityType == EventActivityType.Submission);
            if (subAct == null) return null;
            var voteAct = activities?.FirstOrDefault(a => !a.IsDeleted && a.ActivityType == EventActivityType.Voting);
            var cutoff = subAct.ReviewCutoffAt ?? subAct.EndAt;
            return voteAct != null && voteAct.StartAt < cutoff ? voteAct.StartAt : cutoff;
        }

        var creatorTrack = tracks?.FirstOrDefault(t => !t.IsDeleted && t.TrackType == EventTrackType.Creator);
        var legacyCutoff = ev.ReviewCutoffAt ?? ev.SubmissionCutoffAt ?? creatorTrack?.ExecutionEndAt;
        if (ev.Format == EventFormat.SharedCreatorParent)
        {
            var parentTrack = tracks?.FirstOrDefault(t => !t.IsDeleted && t.TrackType == EventTrackType.Parent);
            var votingStart = ev.VotingStartAt ?? parentTrack?.ExecutionStartAt;
            return votingStart.HasValue && (!legacyCutoff.HasValue || votingStart.Value < legacyCutoff.Value)
                ? votingStart
                : legacyCutoff;
        }

        return legacyCutoff;
    }

    private static ModerationItemEventSubmissionDto ToDto(ActiveEventSubmissionLink link) => new()
    {
        EventId = link.Event.Id,
        EventTitle = link.Event.Title,
        ReviewCutoffAt = link.ReviewCutoffAt,
        EventSubmissionId = link.Submission.Id
    };
}
