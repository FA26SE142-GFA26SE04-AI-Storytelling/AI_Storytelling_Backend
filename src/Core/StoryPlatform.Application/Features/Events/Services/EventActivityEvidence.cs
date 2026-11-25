using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Time;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.Events.Services;

/// <summary>Reconcile automatic tasks from authoritative records; never trust client supplied progress.</summary>
internal static class EventActivityEvidence
{
    public static Task<IReadOnlyList<EventTaskProgress>> LoadAsync(IUnitOfWork uow,
        EventParticipation participation, EventTrack track, IReadOnlyList<EventTaskDefinition> tasks, CancellationToken ct) =>
        LoadWindowAsync(uow, participation, track.ExecutionStartAt, track.ExecutionEndAt, null, tasks, ct);

    public static Task<IReadOnlyList<EventTaskProgress>> LoadForActivityAsync(IUnitOfWork uow,
        EventParticipation participation, EventActivity activity, IReadOnlyList<EventTaskDefinition> tasks, CancellationToken ct) =>
        LoadWindowAsync(uow, participation, activity.StartAt, activity.EndAt, activity.StorySource, tasks, ct);

    private static async Task<IReadOnlyList<EventTaskProgress>> LoadWindowAsync(IUnitOfWork uow,
        EventParticipation participation, DateTime windowStart, DateTime windowEnd, EventReadingStorySource? storySource,
        IReadOnlyList<EventTaskDefinition> tasks, CancellationToken ct)
    {
        var stored = await uow.Repository<EventTaskProgress>().FindAsync(x => x.ParticipationId == participation.Id, cancellationToken: ct);
        var manualIds = tasks.Where(t => t.VerifierType is TaskVerifierType.GuidedDiscussionCheckin or TaskVerifierType.RealLifePracticeCheckin)
            .Select(t => t.Id).ToHashSet();
        var start = participation.CreatedAt > windowStart ? participation.CreatedAt : windowStart;
        var end = windowEnd < DateTime.UtcNow ? windowEnd : DateTime.UtcNow;
        var result = stored.Where(p => manualIds.Contains(p.TaskDefinitionId) && p.CompletedAt >= start && p.CompletedAt <= end).ToList();
        if (end < start) return result;

        var children = await uow.Repository<ChildProfile>().FindAsync(x => x.OwnerUserId == participation.UserId && !x.IsDeleted, cancellationToken: ct);
        var childIds = children.Select(x => x.Id).ToList();
        var links = await uow.Repository<EventStory>().FindAsync(x => x.EventId == participation.EventId && x.IsActive, cancellationToken: ct);
        var readable = links.Where(link => EventParentReadiness.IncludesReadableSource(link.Source, storySource));
        var storyIds = readable.Select(x => x.StoryId).Distinct().ToList();
        var filters = tasks.ToDictionary(t => t.Id, t => EventTaskFilters.Parse(t.ConfigJson));
        var categories = filters.Values.Any(f => f.CategoryIds is not null)
            ? await EventTaskFilters.LoadCategoriesAsync(uow, links.Select(link => link.StoryId).Distinct().ToList(), ct)
            : EventTaskFilters.NoCategories;
        result.RemoveAll(progress => !progress.StoryId.HasValue || !storyIds.Contains(progress.StoryId.Value) ||
            !filters[progress.TaskDefinitionId].Includes(progress.StoryId.Value, categories));
        var sessions = await uow.Repository<ReadingSession>().FindAsync(x => childIds.Contains(x.ChildProfileId) && storyIds.Contains(x.StoryId), cancellationToken: ct);
        var sessionIds = sessions.Select(x => x.Id).ToList();
        var bySession = sessions.ToDictionary(x => x.Id);
        var evidence = new List<(TaskVerifierType Type, int StoryId, DateTime At, int ReadSeconds)>();
        var includeActiveDays = tasks.Any(t => t.VerifierType == TaskVerifierType.ActiveDaysCount);
        var completedReads = sessions.Where(x => x.Status == SessionStatus.Completed && x.CompletedAt >= start && x.CompletedAt <= end).ToList();

        foreach (var s in completedReads)
            evidence.Add((TaskVerifierType.ReadEventStories, s.StoryId, s.CompletedAt!.Value, s.TimeSpentSeconds));

        if (includeActiveDays || tasks.Any(t => t.VerifierType == TaskVerifierType.CompleteStoryQuiz))
        {
            var learningStories = (await uow.Repository<Story>().FindAsync(
                x => storyIds.Contains(x.Id) && x.OutputMode == StoryOutputMode.Learning, cancellationToken: ct)).Select(x => x.Id).ToHashSet();
            var attempts = await uow.Repository<QuizAttempt>().FindAsync(x => sessionIds.Contains(x.ReadingSessionId) && x.AnsweredAt >= start && x.AnsweredAt <= end, cancellationToken: ct);
            var versions = sessions.Where(x => x.StoryVersionId.HasValue).Select(x => x.StoryVersionId!.Value).Distinct().ToList();
            var items = await uow.Repository<QuizItem>().FindAsync(x => versions.Contains(x.StoryVersionId), cancellationToken: ct);
            foreach (var s in sessions.Where(x => x.StoryVersionId.HasValue && learningStories.Contains(x.StoryId)))
            {
                var required = items.Where(x => x.StoryVersionId == s.StoryVersionId && x.ParentStoryPackageId == s.ParentStoryPackageId).Select(x => x.Id).ToArray();
                var answers = attempts.Where(x => x.ReadingSessionId == s.Id && !string.IsNullOrWhiteSpace(x.AnswerGiven)).ToList();
                if (required.Length > 0 && required.All(id => answers.Any(a => a.QuizItemId == id)))
                    evidence.Add((TaskVerifierType.CompleteStoryQuiz, s.StoryId, answers.Max(a => a.AnsweredAt), 0));
            }
        }

        if (includeActiveDays || tasks.Any(t => t.VerifierType == TaskVerifierType.SaveStoryVocabulary))
        {
            var entries = await uow.Repository<VocabularyNotebookEntry>().FindAsync(x => childIds.Contains(x.ChildProfileId) && x.RemovedAt == null && x.CollectedAt >= start && x.CollectedAt <= end, cancellationToken: ct);
            var ids = entries.Select(x => x.StoryVocabularyId).ToList();
            var words = await uow.Repository<StoryVocabulary>().FindAsync(x => ids.Contains(x.Id), cancellationToken: ct);
            var versionIds = words.Select(x => x.StoryVersionId).Distinct().ToList();
            var versions = await uow.Repository<StoryVersion>().FindAsync(x => versionIds.Contains(x.Id) && storyIds.Contains(x.StoryId), cancellationToken: ct);
            foreach (var entry in entries.GroupBy(x => x.StoryVocabularyId).Select(x => x.OrderBy(e => e.CollectedAt).First()))
            {
                var word = words.FirstOrDefault(x => x.Id == entry.StoryVocabularyId);
                var version = versions.FirstOrDefault(x => x.Id == word?.StoryVersionId);
                if (version != null) evidence.Add((TaskVerifierType.SaveStoryVocabulary, version.StoryId, entry.CollectedAt, 0));
            }
        }

        if (includeActiveDays || tasks.Any(t => t.VerifierType == TaskVerifierType.AnswerChildQuestion))
        {
            var summaries = await uow.Repository<StorySummary>().FindAsync(x => sessionIds.Contains(x.ReadingSessionId), cancellationToken: ct);
            var summaryIds = summaries.Select(x => x.Id).ToList();
            var flags = await uow.Repository<StorySummaryFlag>().FindAsync(x => summaryIds.Contains(x.StorySummaryId) && x.AnsweredAt >= start && x.AnsweredAt <= end, cancellationToken: ct);
            foreach (var flag in flags)
            {
                var summary = summaries.First(x => x.Id == flag.StorySummaryId);
                evidence.Add((TaskVerifierType.AnswerChildQuestion, bySession[summary.ReadingSessionId].StoryId, flag.AnsweredAt!.Value, 0));
            }
        }

        if (includeActiveDays || tasks.Any(t => t.VerifierType == TaskVerifierType.RateAndReviewStory))
        {
            var ratings = await uow.Repository<StoryRating>().FindAsync(x => x.ReviewerUserId == participation.UserId && storyIds.Contains(x.StoryId)
                && x.Status == StoryRatingStatus.Published && !x.IsDeleted && x.CreatedAt >= start && x.CreatedAt <= end, cancellationToken: ct);
            evidence.AddRange(ratings.Where(rating => completedReads.Any(s => s.StoryId == rating.StoryId && s.CompletedAt <= rating.CreatedAt))
                .Select(x => (TaskVerifierType.RateAndReviewStory, x.StoryId, x.CreatedAt, 0)));
        }

        if (includeActiveDays || tasks.Any(t => t.VerifierType == TaskVerifierType.VoteEventStories))
        {
            var votes = await uow.Repository<EventVote>().FindAsync(
                x => x.EventId == participation.EventId && x.ParentUserId == participation.UserId &&
                     !x.IsDisqualified && !x.IsDeleted && x.CreatedAt >= start && x.CreatedAt <= end,
                cancellationToken: ct);
            evidence.AddRange(votes.Select(v => (TaskVerifierType.VoteEventStories, v.StoryId, v.UpdatedAt ?? v.CreatedAt, 0)));
        }

        if (tasks.Any(t => t.VerifierType == TaskVerifierType.SubmitApprovedStory))
        {
            var submissions = await uow.Repository<EventSubmission>().FindAsync(s => s.ParticipationId == participation.Id &&
                s.EventId == participation.EventId && s.Status == EventSubmissionStatus.Approved && s.LockedStoryVersionId != null,
                cancellationToken: ct);
            evidence.AddRange(submissions.Select(s => (TaskVerifierType.SubmitApprovedStory, s.StoryId, s.SubmittedAt ?? s.CreatedAt, 0)));
        }

        // Active days reflect real reading activity, not the date of a dashboard request.
        var activeDates = evidence.Select(e => (e.StoryId, e.At)).Where(e => e.At >= start && e.At <= end)
            .Concat(result.Select(p => (StoryId: p.StoryId ?? 0, At: p.CompletedAt))).ToList();
        foreach (var task in tasks.Where(t => !manualIds.Contains(t.Id)))
        {
            var filter = filters[task.Id];
            var values = (task.VerifierType == TaskVerifierType.ActiveDaysCount
                ? activeDates
                : evidence.Where(e => e.Type == task.VerifierType && (e.Type != TaskVerifierType.ReadEventStories || e.ReadSeconds >= filter.MinReadSeconds))
                    .Select(e => (e.StoryId, e.At)).ToList())
                .Where(e => filter.Includes(e.StoryId, categories)).ToList();
            if (task.VerifierType == TaskVerifierType.CompleteStoryQuiz) values = values.DistinctBy(x => x.StoryId).ToList();
            foreach (var value in values.Where(v => v.At >= start && v.At <= end))
                result.Add(new EventTaskProgress { TaskDefinitionId = task.Id, StoryId = value.StoryId,
                    CompletedAt = value.At, ActivityLocalDate = DateOnly.FromDateTime(VietnamTime.LocalDate(value.At)) });
        }
        return result;
    }
}
