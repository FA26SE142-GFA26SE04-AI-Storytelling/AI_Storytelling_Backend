using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Application.Features.ReadingSessions;

/// <summary>Một lần làm quiz: số câu đã trả lời và số câu đúng.</summary>
public sealed record QuizRun(int Answered, int Correct);

public static class QuizRunCalculator
{
    /// <summary>
    /// Tách lịch sử trả lời (đã sắp theo thứ tự thời gian) thành các lần làm. Trả lời lại một câu đã có trong lần
    /// hiện tại nghĩa là trẻ bắt đầu một lần làm mới.
    /// </summary>
    public static IReadOnlyList<QuizRun> Split(IEnumerable<QuizAttempt> attemptsInOrder)
    {
        var runs = new List<QuizRun>();
        var seen = new HashSet<int>();
        var correct = 0;
        foreach (var attempt in attemptsInOrder)
        {
            if (seen.Contains(attempt.QuizItemId))
            {
                runs.Add(new QuizRun(seen.Count, correct));
                seen.Clear();
                correct = 0;
            }

            seen.Add(attempt.QuizItemId);
            if (attempt.IsCorrect == true)
            {
                correct++;
            }
        }

        if (seen.Count > 0)
        {
            runs.Add(new QuizRun(seen.Count, correct));
        }

        return runs;
    }

    public static decimal Percent(int correct, int totalQuestions) =>
        totalQuestions <= 0 ? 0m : Math.Round(correct * 100m / totalQuestions, 1);

    /// <summary>Phần trăm của lần làm tốt nhất; null nếu chưa làm lần nào. Câu bỏ qua tính là chưa đúng.</summary>
    public static decimal? BestPercent(IReadOnlyList<QuizRun> runs, int totalQuestions) =>
        runs.Count == 0 ? null : runs.Max(run => Percent(run.Correct, totalQuestions));
}
