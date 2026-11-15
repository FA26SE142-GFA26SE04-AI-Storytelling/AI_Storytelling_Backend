using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Application.Features.ReadingSessions.Services;

public partial class ParentReadingFeedbackService
{
    public async Task<ParentQuizDetailDto> GetQuizAsync(
        int parentUserId, int childProfileId, int sessionId, CancellationToken ct = default)
    {
        await _guard.EnsureOwnerAsync(childProfileId, parentUserId, ct);
        var session = await _unitOfWork.Repository<ReadingSession>().GetByIdAsync(sessionId, ct);
        if (session == null || session.IsDeleted || session.ChildProfileId != childProfileId)
        {
            throw new NotFoundException("Phiên đọc", sessionId);
        }

        var versionIds = session.StoryVersionId.HasValue
            ? new List<int> { session.StoryVersionId.Value }
            : new List<int>();
        var childSessionIds = (await _unitOfWork.Repository<ReadingSession>().FindAsync(
                other => other.ChildProfileId == childProfileId
                         && other.StoryId == session.StoryId
                         && !other.IsDeleted,
                cancellationToken: ct))
            .Select(other => other.Id).ToList();
        var quiz = await LoadQuizAsync(versionIds, childSessionIds, ct);
        var items = quiz.ItemsOf(session.StoryVersionId);
        var runs = quiz.RunsOf(session.StoryVersionId);
        var title = session.StoryVersionId.HasValue
            ? (await _unitOfWork.Repository<StoryVersion>().GetByIdAsync(session.StoryVersionId.Value, ct))?.Title
            : null;

        return new ParentQuizDetailDto
        {
            SessionId = session.Id,
            StoryId = session.StoryId,
            Title = title ?? string.Empty,
            TotalQuestions = items.Count,
            Runs = runs.Select((run, index) => new ParentQuizRunDto
            {
                RunNo = index + 1,
                Answered = run.Answered,
                Correct = run.Correct,
                Percent = QuizRunCalculator.Percent(run.Correct, items.Count)
            }).ToList(),
            Questions = items.Select(item =>
            {
                var answers = quiz.Attempts.Where(attempt => attempt.QuizItemId == item.Id).ToList();
                var latest = answers.LastOrDefault();
                return new ParentQuizQuestionDto
                {
                    QuizItemId = item.Id,
                    Type = item.Type.ToString(),
                    Question = item.Question,
                    Choices = QuizRules.ParseChoices(item.Type, item.Choices).ToList(),
                    CorrectAnswer = item.CorrectAnswer,
                    Explanation = item.Explanation,
                    LatestAnswer = latest?.AnswerGiven,
                    LatestIsCorrect = latest?.IsCorrect,
                    TimesAnswered = answers.Count,
                    TimesCorrect = answers.Count(attempt => attempt.IsCorrect == true)
                };
            }).ToList()
        };
    }

    /// <summary>Câu đố theo version và lịch sử trả lời của bé trên các phiên đã cho (đã sắp theo Id).</summary>
    private sealed record QuizData(Dictionary<int, List<QuizItem>> ItemsByVersion, List<QuizAttempt> Attempts)
    {
        public List<QuizItem> ItemsOf(int? versionId) =>
            versionId.HasValue && ItemsByVersion.TryGetValue(versionId.Value, out var items)
                ? items
                : new List<QuizItem>();

        public IReadOnlyList<QuizRun> RunsOf(int? versionId)
        {
            var ids = ItemsOf(versionId).Select(item => item.Id).ToHashSet();
            return QuizRunCalculator.Split(Attempts.Where(attempt => ids.Contains(attempt.QuizItemId)));
        }
    }

    private async Task<QuizData> LoadQuizAsync(List<int> versionIds, List<int> childSessionIds, CancellationToken ct)
    {
        var items = (await _unitOfWork.Repository<QuizItem>().FindAsync(
                item => versionIds.Contains(item.StoryVersionId) && !item.IsDeleted, cancellationToken: ct))
            .OrderBy(item => item.Id)
            .GroupBy(item => item.StoryVersionId)
            .ToDictionary(group => group.Key, group => group.ToList());
        var allItemIds = items.Values.SelectMany(list => list).Select(item => item.Id).ToList();
        if (allItemIds.Count == 0 || childSessionIds.Count == 0)
        {
            return new QuizData(items, new List<QuizAttempt>());
        }

        var attempts = (await _unitOfWork.Repository<QuizAttempt>().FindAsync(
                attempt => childSessionIds.Contains(attempt.ReadingSessionId)
                           && allItemIds.Contains(attempt.QuizItemId)
                           && !attempt.IsDeleted,
                cancellationToken: ct))
            .OrderBy(attempt => attempt.Id).ToList();
        return new QuizData(items, attempts);
    }
}
