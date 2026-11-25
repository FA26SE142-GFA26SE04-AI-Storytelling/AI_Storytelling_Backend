using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.Events.Services;

internal static class EventParentReadiness
{
    public static bool IsWaitingForSubmissions(EventActivity reading, EventActivity? submission, DateTime now)
        => reading.StorySource != EventReadingStorySource.CuratedOnly &&
           submission != null && submission.AllowedAudience != EventAudience.Parent &&
           reading.StartAt >= (submission.ReviewCutoffAt ?? submission.EndAt) && now < reading.StartAt;

    public static bool IncludesReadableSource(EventStorySource source, EventReadingStorySource? readingSource)
        => source != EventStorySource.ParentSubmission && (readingSource switch
        {
            EventReadingStorySource.CuratedOnly => source == EventStorySource.ModeratorSeed,
            EventReadingStorySource.ApprovedSubmissionsOnly => source == EventStorySource.CreatorSubmission,
            _ => true
        });

    public static Task EnsureAsync(IUnitOfWork uow, int eventId, CancellationToken ct) =>
        EnsureWithSourceAsync(uow, eventId, null, ct);

    public static Task EnsureActivityReadyAsync(IUnitOfWork uow, Event ev, EventActivity readingActivity, CancellationToken ct) =>
        EnsureWithSourceAsync(uow, ev.Id, readingActivity.StorySource, ct);

    private static async Task EnsureWithSourceAsync(IUnitOfWork uow, int eventId, EventReadingStorySource? storySource, CancellationToken ct)
    {
        var rule = await uow.Repository<EventRuleVersion>().FirstOrDefaultAsync(x => x.EventId == eventId && x.IsActive, cancellationToken: ct)
            ?? throw new ConflictException("Sự kiện chưa có thể lệ đang hoạt động.");
        var tasks = await uow.Repository<EventTaskDefinition>().FindAsync(x => x.EventRuleVersionId == rule.Id, cancellationToken: ct);
        var links = await uow.Repository<EventStory>().FindAsync(x => x.EventId == eventId && x.IsActive && x.Source != EventStorySource.ParentSubmission, cancellationToken: ct);
        var ids = links.Where(x => IncludesReadableSource(x.Source, storySource)).Select(x => x.StoryId).Distinct().ToList();
        var stories = await uow.Repository<Story>().FindAsync(x => ids.Contains(x.Id) && x.IsPublished, cancellationToken: ct);
        var requirements = tasks.Where(t => (t.TargetTrackType == null || t.TargetTrackType == EventTrackType.Parent) &&
            t.VerifierType is TaskVerifierType.ReadEventStories or TaskVerifierType.VoteEventStories or
            TaskVerifierType.CompleteStoryQuiz or TaskVerifierType.SaveStoryVocabulary)
            .Select(task => (Task: task, Filter: EventTaskFilters.Parse(task.ConfigJson))).ToList();
        var categories = requirements.Any(x => x.Filter.CategoryIds is not null)
            ? await EventTaskFilters.LoadCategoriesAsync(uow, ids, ct)
            : EventTaskFilters.NoCategories;
        foreach (var (task, filter) in requirements)
        {
            var eligible = stories.Count(story => filter.Includes(story.Id, categories) &&
                (task.VerifierType is TaskVerifierType.ReadEventStories or TaskVerifierType.VoteEventStories || story.OutputMode == StoryOutputMode.Learning));
            if (eligible < task.TargetValue)
                throw new ConflictException($"Nhiệm vụ {task.TaskCode} ({task.Title}) cần {task.TargetValue} truyện phù hợp, hiện có {eligible}. Hãy bổ sung truyện đúng bộ lọc hoặc điều chỉnh chỉ tiêu trước khi mở hoạt động đọc (BR-TK03/04).");
        }
    }
}
