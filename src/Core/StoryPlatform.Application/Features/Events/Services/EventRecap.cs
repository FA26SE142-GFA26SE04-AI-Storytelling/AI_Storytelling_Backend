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

internal static class EventRecap
{
    public static async Task<EventRecapDto> BuildAsync(IUnitOfWork uow, Event ev, int userId, CancellationToken ct)
    {
        var activities = await uow.Repository<EventActivity>().FindAsync(
            a => a.EventId == ev.Id && !a.IsDeleted, cancellationToken: ct);
        var (windowStart, windowEnd) = ResolveWindow(ev, activities);

        var storiesRead = await CountStoriesReadAsync(uow, ev.Id, userId, windowStart, windowEnd, ct);

        var subRepo = uow.Repository<EventSubmission>();
        var submissions = subRepo == null
            ? 0
            : await subRepo.CountAsync(s => s.EventId == ev.Id && s.CreatorUserId == userId && !s.IsDeleted, ct);

        var voteRepo = uow.Repository<EventVote>();
        var votes = voteRepo == null
            ? 0
            : await voteRepo.CountAsync(v => v.EventId == ev.Id && v.ParentUserId == userId && !v.IsDeleted, ct);

        var (tasksCompleted, tasksTotal) = await CountMissionTasksAsync(uow, ev.Id, userId, activities, ct);

        return new EventRecapDto
        {
            StoriesRead = storiesRead,
            Submissions = submissions,
            Votes = votes,
            TasksCompleted = tasksCompleted,
            TasksTotal = tasksTotal
        };
    }

    internal static (DateTime? Start, DateTime? End) ResolveWindow(Event ev, IReadOnlyList<EventActivity> activities)
    {
        var start = ev.OpensAt ?? (activities.Count > 0 ? activities.Min(a => a.StartAt) : (DateTime?)null);
        var end = ev.ClosesAt ?? (activities.Count > 0 ? activities.Max(a => a.EndAt) : (DateTime?)null);
        return (start, end);
    }

    private static async Task<int> CountStoriesReadAsync(
        IUnitOfWork uow, int eventId, int userId, DateTime? windowStart, DateTime? windowEnd, CancellationToken ct)
    {
        var childRepo = uow.Repository<ChildProfile>();
        var eventStoryRepo = uow.Repository<EventStory>();
        var sessionRepo = uow.Repository<ReadingSession>();
        if (childRepo == null || eventStoryRepo == null || sessionRepo == null) return 0;

        var children = await childRepo.FindAsync(c => c.OwnerUserId == userId && !c.IsDeleted, cancellationToken: ct);
        if (children == null || children.Count == 0) return 0;

        var eventStories = await eventStoryRepo.FindAsync(es => es.EventId == eventId && es.IsActive && !es.IsDeleted, cancellationToken: ct);
        if (eventStories == null || eventStories.Count == 0) return 0;

        var childIds = children.Select(c => c.Id).ToHashSet();
        var storyIds = eventStories.Select(es => es.StoryId).Distinct().ToHashSet();

        var sessions = await sessionRepo.FindAsync(
            r => childIds.Contains(r.ChildProfileId) && storyIds.Contains(r.StoryId) &&
                 r.Status == SessionStatus.Completed && r.CompletedAt != null,
            cancellationToken: ct);
        if (sessions == null || sessions.Count == 0) return 0;

        return sessions.Count(r =>
            (!windowStart.HasValue || r.CompletedAt!.Value >= windowStart.Value) &&
            (!windowEnd.HasValue || r.CompletedAt!.Value <= windowEnd.Value));
    }

    private static async Task<(int Completed, int Total)> CountMissionTasksAsync(
        IUnitOfWork uow, int eventId, int userId, IReadOnlyList<EventActivity> activities, CancellationToken ct)
    {
        var readAct = activities.FirstOrDefault(a => a.ActivityType == EventActivityType.ReadingMissions);
        if (readAct == null) return (0, 0);

        var part = await uow.Repository<EventParticipation>().FirstOrDefaultAsync(
            p => p.EventId == eventId && p.EventActivityId == readAct.Id && p.UserId == userId && !p.IsDeleted,
            cancellationToken: ct);
        if (part == null) return (0, 0);

        var rule = await uow.Repository<EventRuleVersion>().GetByIdAsync(part.AcceptedRuleVersionId, ct);
        if (rule == null) return (0, 0);

        var defs = await uow.Repository<EventTaskDefinition>().FindAsync(
            t => t.EventRuleVersionId == rule.Id && !t.IsDeleted, cancellationToken: ct);
        if (defs == null || defs.Count == 0) return (0, 0);

        var progresses = await uow.Repository<EventTaskProgress>().FindAsync(
            tp => tp.ParticipationId == part.Id && !tp.IsDeleted, cancellationToken: ct);
        var summary = EventMissionEvaluator.Evaluate(rule, defs, progresses ?? Array.Empty<EventTaskProgress>(), enforceMandatoryInAllModes: true);
        return (summary.CompletedCount, defs.Count);
    }
}
