using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ReadingSessions.Services;

public partial class ParentReadingFeedbackService
{
    public const string NoFeelingKey = "None";

    public async Task<ParentReadingOverviewDto> GetOverviewAsync(
        int parentUserId, int childProfileId, ParentReadingOverviewQueryDto query, CancellationToken ct = default)
    {
        await _guard.EnsureOwnerAsync(childProfileId, parentUserId, ct);
        ParentReadingFeedbackFilter.EnsureRange(query.From, query.To);

        var repository = _unitOfWork.Repository<ReadingSession>();
        var completed = (await repository.FindAsync(
            ParentReadingFeedbackFilter.Sessions(childProfileId, false, query.From, query.To),
            cancellationToken: ct)).ToList();
        var unfinished = (await repository.FindAsync(
            ParentReadingFeedbackFilter.Sessions(childProfileId, true, query.From, query.To),
            cancellationToken: ct)).ToList();
        var inRange = completed.Concat(unfinished).ToList();
        var inRangeIds = inRange.Select(session => session.Id).ToList();

        var storyIds = inRange.Select(session => session.StoryId).Distinct().ToList();
        var storySessions = storyIds.Count == 0
            ? new List<ReadingSession>()
            : (await repository.FindAsync(
                session => session.ChildProfileId == childProfileId
                           && storyIds.Contains(session.StoryId)
                           && !session.IsDeleted,
                cancellationToken: ct)).ToList();
        var unfinishedStories = unfinished
            .Where(open => !storySessions.Any(done => done.StoryId == open.StoryId
                                                      && done.Status == SessionStatus.Completed
                                                      && done.CompletedAt > open.StartedAt))
            .Select(open => open.StoryId)
            .Distinct()
            .Count();

        // Mỗi truyện đọc xong trong khoảng: điểm tốt nhất trên mọi lần làm (giống danh sách), theo bản đọc gần nhất.
        var versionIds = completed.Where(session => session.StoryVersionId.HasValue)
            .Select(session => session.StoryVersionId!.Value).Distinct().ToList();
        var quiz = await LoadQuizAsync(versionIds, storySessions.Select(session => session.Id).ToList(), ct);
        var bestPercents = completed
            .GroupBy(session => session.StoryId)
            .Select(group => group.OrderByDescending(session => session.CompletedAt)
                .ThenByDescending(session => session.Id).First().StoryVersionId)
            .Select(versionId =>
            {
                var count = quiz.ItemsOf(versionId).Count;
                return count == 0 ? null : QuizRunCalculator.BestPercent(quiz.RunsOf(versionId), count);
            })
            .Where(percent => percent.HasValue)
            .Select(percent => percent!.Value)
            .ToList();

        var activity = await LoadActivityLogsAsync(inRangeIds, ct);
        var readAloud = activity.Values
            .Select(logs => ReadingActivityTelemetry.ReadAloud(logs))
            .Where(summary => summary != null)
            .Select(summary => summary!)
            .ToList();
        var goodWords = readAloud.Sum(summary => summary.GoodWords);
        var totalWords = readAloud.Sum(summary => summary.TotalWords);
        var retells = await LoadRetellsAsync(inRangeIds, ct);

        return new ParentReadingOverviewDto
        {
            From = query.From,
            To = query.To,
            CompletedSessions = completed.Count,
            DistinctStoriesCompleted = completed.Select(session => session.StoryId).Distinct().Count(),
            UnfinishedStories = unfinishedStories,
            TotalTimeSpentSeconds = (int)Math.Min(inRange.Sum(session => (long)session.TimeSpentSeconds), int.MaxValue),
            QuizStoriesTaken = bestPercents.Count,
            AverageQuizBestPercent = bestPercents.Count == 0 ? null : Math.Round(bestPercents.Average(), 1),
            FeelingCounts = CountFeelings(completed),
            ExpEarned = activity.Values.Sum(logs => ReadingActivityTelemetry.Exp(logs)),
            WordsCollected = activity.Values
                .SelectMany(logs => ReadingActivityTelemetry.CollectedVocabularyIds(logs))
                .Distinct()
                .Count(),
            ReadAloudGoodPercent = totalWords == 0 ? null : QuizRunCalculator.Percent(goodWords, totalWords),
            RetellCount = retells.Count
        };
    }

    private static Dictionary<string, int> CountFeelings(IEnumerable<ReadingSession> completed)
    {
        var counts = Enum.GetValues<StoryFeeling>().ToDictionary(feeling => feeling.ToString(), _ => 0);
        counts[NoFeelingKey] = 0;
        foreach (var session in completed)
        {
            counts[session.Feeling?.ToString() ?? NoFeelingKey]++;
        }

        return counts;
    }
}
