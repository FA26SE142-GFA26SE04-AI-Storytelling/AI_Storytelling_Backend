using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.Events.DTOs;
using StoryPlatform.Application.Features.Events.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.Events.Services;

public sealed class EventResultsQueryService : IEventResultsQueryService
{
    private readonly IUnitOfWork _uow;

    public EventResultsQueryService(IUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<IReadOnlyList<MyEventResultDto>> GetMyResultsAsync(int userId, CancellationToken ct = default)
    {
        var userParts = await _uow.Repository<EventParticipation>().FindAsync(
            p => p.UserId == userId && p.EventActivityId != null && !p.IsDeleted, cancellationToken: ct);
        if (userParts.Count == 0) return Array.Empty<MyEventResultDto>();

        var user = await _uow.Repository<UserAccount>().GetByIdAsync(userId, ct);
        var isParent = user?.Role == UserRole.User;
        var now = DateTime.UtcNow;

        var eventIds = userParts.Select(p => p.EventId).Distinct().ToList();
        var events = await _uow.Repository<Event>().FindAsync(
            e => eventIds.Contains(e.Id) && e.ConfigVersion == EventConfigVersion.ActivityBased &&
                 !e.IsDeleted && (e.Status == EventStatus.Published || e.Status == EventStatus.Closed ||
                    (e.Status == EventStatus.Paused && e.ResultsPublishedAt != null)),
            cancellationToken: ct);
        if (events.Count == 0) return Array.Empty<MyEventResultDto>();

        var results = new List<MyEventResultDto>();
        foreach (var ev in events.OrderByDescending(e => e.ResultsPublishedAt ?? e.ClosesAt ?? e.CreatedAt))
        {
            var activities = await _uow.Repository<EventActivity>().FindAsync(
                a => a.EventId == ev.Id && !a.IsDeleted, cancellationToken: ct);
            if (ev.Status != EventStatus.Closed &&
                !EventExecution.HasEnded(ev, Array.Empty<EventTrack>(), activities, allowPaused: true))
                continue;

            var actById = activities.ToDictionary(a => a.Id);
            var myPartsForEvent = userParts.Where(p => p.EventId == ev.Id && p.EventActivityId.HasValue).ToList();
            var myPartIds = myPartsForEvent.Select(p => p.Id).ToHashSet();

            var eventAwards = await _uow.Repository<EventAward>().FindAsync(
                a => a.EventId == ev.Id && !a.IsDeleted, cancellationToken: ct);
            var publishedAt = EventExecution.PublishedResultsAt(ev, eventAwards);
            var myAwardEntities = publishedAt.HasValue
                ? eventAwards.Where(a => a.ParticipationId.HasValue && myPartIds.Contains(a.ParticipationId.Value) &&
                    a.Status != EventAwardStatus.AwardProposed).OrderBy(a => a.Id).ToList()
                : new List<EventAward>();

            var appealDeadline = ev.ResultsPublishedAt?.AddHours(EventAppealWindow.Hours);
            var holdsActiveAward = myAwardEntities.Any(a => a.Status is
                EventAwardStatus.AppealHold or EventAwardStatus.AwaitingClaim or
                EventAwardStatus.Claimed or EventAwardStatus.FulfillmentProcessing or EventAwardStatus.Fulfilled);

            var rankingSourceActIds = eventAwards
                .Select(a => ResolveSourceActivityId(a, activities))
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .ToHashSet();

            var appealablePartIds = new HashSet<int>();
            if (ev.Status is (EventStatus.Published or EventStatus.Paused) && ev.ResultsPublishedAt.HasValue &&
                appealDeadline.HasValue && now < appealDeadline.Value && !holdsActiveAward)
            {
                var candidateParts = myPartsForEvent.Where(p =>
                    p.EventActivityId.HasValue && rankingSourceActIds.Contains(p.EventActivityId.Value) &&
                    p.Status is EventParticipationStatus.Verified or EventParticipationStatus.VerificationFailed).ToList();
                if (candidateParts.Count > 0)
                {
                    var candIds = candidateParts.Select(p => p.Id).ToList();
                    var openAppeals = await _uow.Repository<EventAppeal>().FindAsync(
                        a => candIds.Contains(a.ParticipationId) && !a.IsDeleted &&
                             (a.Status == EventAppealStatus.Open || a.Status == EventAppealStatus.UnderReview || a.Status == EventAppealStatus.Escalated),
                        cancellationToken: ct);
                    var openPartIds = openAppeals.Select(a => a.ParticipationId).ToHashSet();
                    var rejectedSubs = await _uow.Repository<EventSubmission>().FindAsync(
                        s => candIds.Contains(s.ParticipationId) && s.Status == EventSubmissionStatus.Rejected && !s.IsDeleted,
                        cancellationToken: ct);
                    var rejectedPartIds = rejectedSubs.Select(s => s.ParticipationId).ToHashSet();
                    appealablePartIds = candidateParts
                        .Where(p => !openPartIds.Contains(p.Id) && !rejectedPartIds.Contains(p.Id))
                        .Select(p => p.Id)
                        .ToHashSet();
                }
            }

            var recap = await EventRecap.BuildAsync(_uow, ev, userId, ct);
            var endedAt = ev.ClosesAt ?? (activities.Count > 0 ? activities.Max(a => a.EndAt) : null);

            results.Add(new MyEventResultDto
            {
                EventId = ev.Id,
                EventTitle = ev.Title,
                EndedAt = endedAt,
                ResultsPublishedAt = publishedAt,
                AppealDeadlineAt = appealDeadline,
                CanAppeal = appealablePartIds.Count > 0,
                Recap = recap,
                Activities = myPartsForEvent
                    .Where(p => p.EventActivityId.HasValue && actById.ContainsKey(p.EventActivityId.Value))
                    .Select(p => new MyActivityResultDto
                    {
                        ParticipationId = p.Id,
                        CanAppeal = appealablePartIds.Contains(p.Id),
                        ActivityType = actById[p.EventActivityId!.Value].ActivityType,
                        Status = p.Status,
                        Reason = p.Status == EventParticipationStatus.Verified ? null : ExtractShortReason(p.VerificationNotesJson)
                    })
                    .ToList(),
                Awards = myAwardEntities.Select(a => new MyAwardDto
                {
                    AwardId = a.Id,
                    RankOrTier = a.RankOrTier,
                    SlotIndex = a.SlotIndex,
                    RewardName = a.RewardName,
                    RewardType = a.RewardType,
                    CreditGrantAmount = a.CreditGrantAmount,
                    SubmitCreditGrantAmount = a.SubmitCreditGrantAmount,
                    UnitValueVnd = a.UnitValueVnd,
                    Status = a.Status,
                    ClaimOpensAt = appealDeadline,
                    ClaimDeadlineAt = a.ClaimDeadlineAt,
                    RevokedReason = a.Status == EventAwardStatus.Revoked ? a.RevokedReason : null,
                    AsksChildNameConsent = isParent && a.ResultSource == EventAwardResultSource.MissionCompletion
                }).ToList()
            });
        }

        return results;
    }

    public async Task<PublicEventResultsDto> GetPublicResultsAsync(int userId, int eventId, CancellationToken ct = default)
    {
        var ev = await _uow.Repository<Event>().GetByIdAsync(eventId, ct)
            ?? throw new NotFoundException(nameof(Event), eventId);
        var eventAwards = await _uow.Repository<EventAward>().FindAsync(
            a => a.EventId == eventId && !a.IsDeleted, cancellationToken: ct);
        var publishedAt = EventExecution.PublishedResultsAt(ev, eventAwards);
        if (!publishedAt.HasValue)
            throw new ConflictException("Kết quả sự kiện chưa được công bố.");

        var activities = await _uow.Repository<EventActivity>().FindAsync(
            a => a.EventId == eventId && !a.IsDeleted, cancellationToken: ct);
        var (windowStart, windowEnd) = EventRecap.ResolveWindow(ev, activities);

        var participations = await _uow.Repository<EventParticipation>().FindAsync(
            p => p.EventId == eventId && p.EventActivityId != null && !p.IsDeleted, cancellationToken: ct);
        var partById = participations.ToDictionary(p => p.Id);
        var participantCount = participations.Select(p => p.UserId).Distinct().Count();

        var eventStories = await _uow.Repository<EventStory>().FindAsync(
            es => es.EventId == eventId && es.IsActive && !es.IsDeleted, cancellationToken: ct);
        var eventStoryIds = eventStories.Select(es => es.StoryId).Distinct().ToHashSet();

        var awards = eventAwards.Where(
            a => a.ParticipationId != null &&
                 a.Status != EventAwardStatus.AwardProposed &&
                 a.Status != EventAwardStatus.Revoked &&
                 a.Status != EventAwardStatus.Expired &&
                 a.Status != EventAwardStatus.UnclaimedBudgetReturned &&
                 a.Status != EventAwardStatus.Failed)
            .OrderBy(a => a.EventActivityId)
            .ThenBy(a => a.SlotIndex)
            .ThenBy(a => a.Id)
            .ToList();

        var winnerPartIds = awards.Select(a => a.ParticipationId!.Value).Distinct().ToList();
        var winnerUserIds = winnerPartIds
            .Where(partById.ContainsKey)
            .Select(pid => partById[pid].UserId)
            .Distinct()
            .ToList();

        var users = winnerUserIds.Count == 0
            ? new Dictionary<int, UserAccount>()
            : (await _uow.Repository<UserAccount>().FindAsync(u => winnerUserIds.Contains(u.Id), cancellationToken: ct))
                .ToDictionary(u => u.Id);

        var subs = winnerPartIds.Count == 0
            ? Array.Empty<EventSubmission>()
            : await _uow.Repository<EventSubmission>().FindAsync(
                s => s.EventId == eventId && winnerPartIds.Contains(s.ParticipationId) &&
                     s.Status == EventSubmissionStatus.Approved && s.LockedStoryVersionId != null && !s.IsDeleted,
                cancellationToken: ct);
        var subByPartId = subs
            .GroupBy(s => s.ParticipationId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(s => s.SubmittedAt ?? s.CreatedAt).ThenByDescending(s => s.Id).First());
        var storyIds = subs.Select(s => s.StoryId).Distinct().ToList();
        var storyTitles = storyIds.Count == 0
            ? new Dictionary<int, string>()
            : (await _uow.Repository<Story>().FindAsync(s => storyIds.Contains(s.Id), cancellationToken: ct))
                .ToDictionary(s => s.Id, s => s.Title ?? string.Empty);

        var missionUserIds = awards
            .Where(a => a.ResultSource == EventAwardResultSource.MissionCompletion &&
                        a.ParticipationId.HasValue && partById.ContainsKey(a.ParticipationId.Value))
            .Select(a => partById[a.ParticipationId!.Value].UserId)
            .Distinct()
            .ToList();

        var participatingChildrenByOwner = new Dictionary<int, List<string>>();
        if (missionUserIds.Count > 0 && eventStoryIds.Count > 0)
        {
            var children = await _uow.Repository<ChildProfile>().FindAsync(
                c => missionUserIds.Contains(c.OwnerUserId) && !c.IsDeleted, cancellationToken: ct);
            var childIds = children.Select(c => c.Id).ToHashSet();
            if (childIds.Count > 0)
            {
                var sessions = await _uow.Repository<ReadingSession>().FindAsync(
                    r => childIds.Contains(r.ChildProfileId) && eventStoryIds.Contains(r.StoryId) &&
                         r.Status == SessionStatus.Completed && r.CompletedAt != null,
                    cancellationToken: ct);
                var activeChildIds = sessions
                    .Where(r => (!windowStart.HasValue || r.CompletedAt!.Value >= windowStart.Value) &&
                                (!windowEnd.HasValue || r.CompletedAt!.Value <= windowEnd.Value))
                    .Select(r => r.ChildProfileId)
                    .ToHashSet();
                foreach (var group in children.Where(c => activeChildIds.Contains(c.Id)).GroupBy(c => c.OwnerUserId))
                {
                    participatingChildrenByOwner[group.Key] = group
                        .OrderBy(c => c.Id)
                        .Select(c => c.Nickname?.Trim() ?? string.Empty)
                        .ToList();
                }
            }
        }

        var publicAwards = new List<PublicAwardDto>(awards.Count);
        foreach (var award in awards)
        {
            if (!award.ParticipationId.HasValue || !partById.TryGetValue(award.ParticipationId.Value, out var part))
                continue;

            var winnerName = users.TryGetValue(part.UserId, out var u)
                ? EventAwardEnricher.FormatDisplayName(u)
                : null;
            string? storyTitle = null;
            if (award.ResultSource is EventAwardResultSource.SubmissionScore or EventAwardResultSource.VotingAverage &&
                subByPartId.TryGetValue(part.Id, out var sub))
            {
                storyTitle = !string.IsNullOrWhiteSpace(sub.ParentSubmissionTitle)
                    ? sub.ParentSubmissionTitle
                    : storyTitles.GetValueOrDefault(sub.StoryId);
            }

            var childCount = 0;
            var childNicknames = new List<string>();
            if (award.ResultSource == EventAwardResultSource.MissionCompletion &&
                participatingChildrenByOwner.TryGetValue(part.UserId, out var nicknames))
            {
                childCount = nicknames.Count;
                if (award.ShowChildNames)
                    childNicknames = nicknames.Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
            }

            publicAwards.Add(new PublicAwardDto
            {
                RankOrTier = award.RankOrTier,
                SlotIndex = award.SlotIndex,
                RewardName = award.RewardName,
                ResultSource = award.ResultSource,
                StoryTitle = storyTitle,
                WinnerName = winnerName,
                ChildNicknames = childNicknames,
                ChildCount = childCount
            });
        }

        return new PublicEventResultsDto
        {
            EventId = ev.Id,
            EventTitle = ev.Title,
            ResultsPublishedAt = publishedAt.Value,
            ParticipantCount = participantCount,
            StoryCount = eventStories.Count,
            Awards = publicAwards
        };
    }

    internal static int? ResolveSourceActivityId(EventAward award, IReadOnlyList<EventActivity> activities)
    {
        if (award.ResultSource is EventAwardResultSource.SubmissionScore or EventAwardResultSource.VotingAverage)
            return activities.FirstOrDefault(a => a.ActivityType == EventActivityType.Submission)?.Id;
        if (award.ResultSource == EventAwardResultSource.MissionCompletion)
            return activities.FirstOrDefault(a => a.ActivityType == EventActivityType.ReadingMissions)?.Id;
        return award.EventActivityId;
    }

    internal static string? ExtractShortReason(string? verificationNotesJson)
    {
        if (string.IsNullOrWhiteSpace(verificationNotesJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(verificationNotesJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            if (root.TryGetProperty("reason", out var reasonProp) && reasonProp.ValueKind == JsonValueKind.String)
            {
                var text = reasonProp.GetString()?.Trim();
                if (!string.IsNullOrEmpty(text)) return text;
            }

            if (root.TryGetProperty("failedTasks", out var failedProp) && failedProp.ValueKind == JsonValueKind.Array)
            {
                var codes = new List<string>();
                foreach (var item in failedProp.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object)
                    {
                        if (item.TryGetProperty("TaskCode", out var tc) && tc.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(tc.GetString()))
                            codes.Add(tc.GetString()!);
                        else if (item.TryGetProperty("taskCode", out var tc2) && tc2.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(tc2.GetString()))
                            codes.Add(tc2.GetString()!);
                    }
                }
                if (codes.Count > 0)
                    return $"Chưa xong nhiệm vụ: {string.Join(", ", codes)}";
            }

            if (root.TryGetProperty("hasVote", out var voteProp) && voteProp.ValueKind == JsonValueKind.False)
                return "Chưa có phiếu bình chọn hợp lệ.";
        }
        catch (JsonException)
        {
            return null;
        }
        return null;
    }
}
