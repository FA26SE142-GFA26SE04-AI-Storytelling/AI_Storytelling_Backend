using System;
using System.Collections.Generic;
using System.Linq;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.Events.Services;

public sealed record TaskEvaluationResult(
    EventTaskDefinition Task,
    int CurrentValue,
    bool IsCompleted);

public sealed record MissionEvaluationSummary(
    IReadOnlyList<TaskEvaluationResult> Tasks,
    int CompletedCount,
    int EarnedPoints,
    bool AllRequirementsMet);

public sealed record CreatorRankingCandidate(
    int SubmissionId,
    int ParticipationId,
    decimal TotalScore,
    decimal PrimaryCriterionScore,
    int RevisionChangesCount,
    DateTime SubmittedAt);

public sealed record CreatorVoteRankingCandidate(
    int SubmissionId,
    int ParticipationId,
    int CreatorUserId,
    int StoryId,
    string StoryTitle,
    int ValidVoteCount,
    decimal AverageStars,
    DateTime FirstSubmittedAt);

/// <summary>
/// Động cơ đánh giá nhiệm vụ động (1..N nhiệm vụ) và xếp hạng Creator tất định cho Event.
/// </summary>
public static class EventMissionEvaluator
{
    public static MissionEvaluationSummary Evaluate(
        EventRuleVersion rule,
        IReadOnlyList<EventTaskDefinition> definitions,
        IReadOnlyList<EventTaskProgress> progresses,
        bool enforceMandatoryInAllModes = false)
    {
        var ordered = definitions.OrderBy(t => t.SortOrder).ThenBy(t => t.Id).ToList();
        var results = new List<TaskEvaluationResult>(ordered.Count);

        foreach (var task in ordered)
        {
            var items = progresses.Where(p => p.TaskDefinitionId == task.Id).ToList();
            int currentValue = task.VerifierType switch
            {
                TaskVerifierType.ActiveDaysCount => items
                    .Select(p => p.ActivityLocalDate)
                    .Distinct()
                    .Count(),
                TaskVerifierType.ReadEventStories or TaskVerifierType.RateAndReviewStory
                    or TaskVerifierType.VoteEventStories or TaskVerifierType.SubmitApprovedStory => items
                    .Where(p => p.StoryId.HasValue)
                    .Select(p => p.StoryId!.Value)
                    .Distinct()
                    .Count(),
                TaskVerifierType.GuidedDiscussionCheckin or TaskVerifierType.RealLifePracticeCheckin => task.AllowMultiplePerStory
                    ? items.Count
                    : items.Select(p => p.StoryId ?? p.Id).Distinct().Count(),
                _ => items.Count
            };

            results.Add(new TaskEvaluationResult(task, currentValue, currentValue >= Math.Max(1, task.TargetValue)));
        }

        var completedCount = results.Count(r => r.IsCompleted);
        var earnedPoints = results.Where(r => r.IsCompleted).Sum(r => r.Task.Points);

        var allMandatoryPassed = results.Where(r => r.Task.IsMandatory).All(r => r.IsCompleted);
        bool met = rule.CompletionMode switch
        {
            TrackCompletionMode.AllMandatory => ordered.Count > 0 && allMandatoryPassed,
            TrackCompletionMode.MinimumTasksCount => allMandatoryPassed && completedCount >= Math.Max(1, rule.RequiredTaskCount ?? ordered.Count),
            TrackCompletionMode.MinimumPointsThreshold => (!enforceMandatoryInAllModes || allMandatoryPassed) && earnedPoints >= Math.Max(1, rule.RequiredPoints ?? 1),
            _ => false
        };

        return new MissionEvaluationSummary(results, completedCount, earnedPoints, met);
    }

    public static IReadOnlyList<CreatorRankingCandidate> RankCreatorSubmissions(IEnumerable<CreatorRankingCandidate> candidates) =>
        candidates
            .OrderByDescending(c => c.TotalScore)
            .ThenByDescending(c => c.PrimaryCriterionScore)
            .ThenBy(c => c.RevisionChangesCount)
            .ThenBy(c => c.SubmittedAt)
            .ThenBy(c => c.SubmissionId)
            .ToList();

    public static IReadOnlyList<CreatorVoteRankingCandidate> RankCreatorByVotes(
        IEnumerable<CreatorVoteRankingCandidate> candidates,
        int minVotesPerSubmission) =>
        candidates
            .Where(c => c.ValidVoteCount >= Math.Max(1, minVotesPerSubmission))
            .OrderByDescending(c => c.AverageStars)
            .ThenByDescending(c => c.ValidVoteCount)
            .ThenBy(c => c.FirstSubmittedAt)
            .ThenBy(c => c.SubmissionId)
            .ToList();

    /// <summary>Tính độ tương đồng Jaccard trên tập 3-gram từ tiếng Việt để cảnh báo trùng lặp nội dung.</summary>
    public static double ComputeJaccardSimilarity(string? leftText, string? rightText, int n = 3)
    {
        var a = BuildWordNgrams(leftText, n);
        var b = BuildWordNgrams(rightText, n);
        if (a.Count == 0 || b.Count == 0) return 0d;

        var intersection = 0;
        foreach (var gram in a)
        {
            if (b.Contains(gram)) intersection++;
        }

        var union = a.Count + b.Count - intersection;
        return union == 0 ? 0d : (double)intersection / union;
    }

    private static HashSet<string> BuildWordNgrams(string? text, int n)
    {
        if (string.IsNullOrWhiteSpace(text)) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var words = text
            .Split([' ', '\t', '\r', '\n', '.', ',', ';', ':', '!', '?', '"', '\'', '(', ')'], StringSplitOptions.RemoveEmptyEntries);
        var grams = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (words.Length < n)
        {
            if (words.Length > 0) grams.Add(string.Join(' ', words));
            return grams;
        }

        for (var i = 0; i <= words.Length - n; i++)
        {
            grams.Add(string.Join(' ', words, i, n));
        }

        return grams;
    }
}
