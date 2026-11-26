using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.Events.DTOs;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.Events.Services;

internal static class EventAwardEnricher
{
    public static EventAwardSlotDto MapBase(
        EventAward a,
        IReadOnlyDictionary<int, EventActivity>? activityMap = null,
        bool includeOperatorFields = false) => new()
    {
        Id = a.Id,
        TrackId = a.TrackId,
        EventActivityId = a.EventActivityId,
        ActivityType = a.EventActivityId.HasValue && activityMap != null && activityMap.TryGetValue(a.EventActivityId.Value, out var act)
            ? act.ActivityType : null,
        ResultSource = a.ResultSource,
        TargetAudience = a.TargetAudience,
        RankOrTier = a.RankOrTier,
        SlotIndex = a.SlotIndex,
        RewardType = a.RewardType,
        RewardName = a.RewardName,
        UnitValueVnd = a.UnitValueVnd,
        CreditGrantAmount = a.CreditGrantAmount,
        SubmitCreditGrantAmount = a.SubmitCreditGrantAmount,
        ParticipationId = includeOperatorFields || a.Status != EventAwardStatus.AwardProposed ? a.ParticipationId : null,
        Status = a.Status,
        ClaimDeadlineAt = a.ClaimDeadlineAt,
        ManualAssignmentNote = includeOperatorFields ? a.ManualAssignmentNote : null,
        IsManualAssignment = includeOperatorFields && a.ManuallyAssignedAt.HasValue,
        RevokedReason = includeOperatorFields ? a.RevokedReason : null,
        RevokedAt = includeOperatorFields ? a.RevokedAt : null
    };

    public static async Task<List<EventAwardSlotDto>> MapForOperatorsAsync(
        IUnitOfWork uow,
        int eventId,
        IReadOnlyList<EventAward> awards,
        IReadOnlyDictionary<int, EventActivity>? activityMap,
        CancellationToken ct)
    {
        var dtos = awards.Select(a => MapBase(a, activityMap, includeOperatorFields: true)).ToList();
        var partIds = awards.Where(a => a.ParticipationId.HasValue).Select(a => a.ParticipationId!.Value).Distinct().ToList();
        if (partIds.Count == 0)
            return dtos;

        var partRepo = uow.Repository<EventParticipation>();
        var userRepo = uow.Repository<UserAccount>();
        var subRepo = uow.Repository<EventSubmission>();
        var storyRepo = uow.Repository<Story>();

        var parts = partRepo == null
            ? Array.Empty<EventParticipation>()
            : (await partRepo.FindAsync(p => partIds.Contains(p.Id), cancellationToken: ct)) ?? Array.Empty<EventParticipation>();
        var partById = parts.ToDictionary(p => p.Id);

        var userIds = parts.Select(p => p.UserId).Distinct().ToList();
        var users = userRepo == null || userIds.Count == 0
            ? Array.Empty<UserAccount>()
            : (await userRepo.FindAsync(u => userIds.Contains(u.Id), cancellationToken: ct)) ?? Array.Empty<UserAccount>();
        var userById = users.ToDictionary(u => u.Id);

        var subs = subRepo == null
            ? Array.Empty<EventSubmission>()
            : (await subRepo.FindAsync(
                s => s.EventId == eventId && partIds.Contains(s.ParticipationId) &&
                     s.Status == EventSubmissionStatus.Approved && s.LockedStoryVersionId != null,
                cancellationToken: ct)) ?? Array.Empty<EventSubmission>();
        var storyIds = subs.Select(s => s.StoryId).Distinct().ToList();
        var stories = storyRepo == null || storyIds.Count == 0
            ? Array.Empty<Story>()
            : (await storyRepo.FindAsync(s => storyIds.Contains(s.Id), cancellationToken: ct)) ?? Array.Empty<Story>();
        var storyById = stories.ToDictionary(s => s.Id, s => s.Title);

        var subByPartId = subs
            .GroupBy(s => s.ParticipationId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(s => s.SubmittedAt ?? s.CreatedAt).ThenByDescending(s => s.Id).First());

        foreach (var dto in dtos)
        {
            if (!dto.ParticipationId.HasValue || !partById.TryGetValue(dto.ParticipationId.Value, out var part))
                continue;

            if (userById.TryGetValue(part.UserId, out var user))
                dto.WinnerName = FormatDisplayName(user);

            if (subByPartId.TryGetValue(part.Id, out var sub) && storyById.TryGetValue(sub.StoryId, out var title))
                dto.WinnerStoryTitle = title;
        }

        return dtos;
    }

    public static string FormatDisplayName(UserAccount user) =>
        string.IsNullOrWhiteSpace(user.FullName) ? user.Username : user.FullName.Trim();
}
