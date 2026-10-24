using System.Text.Json;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ReadingSessions.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ReadingSessions.Services;

public class ChildQuizService : IChildQuizService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IChildRewardService _rewards;

    public ChildQuizService(IUnitOfWork unitOfWork, IChildRewardService rewards)
    {
        _unitOfWork = unitOfWork;
        _rewards = rewards;
    }

    public async Task<QuizDto> GetQuizAsync(int childProfileId, int sessionId, CancellationToken ct = default)
    {
        var context = await LoadAsync(childProfileId, sessionId, ct);
        var runs = QuizRunCalculator.Split(context.Attempts);
        return new QuizDto
        {
            SessionId = context.Session.Id,
            TotalQuestions = context.Items.Count,
            BestPercent = QuizRunCalculator.BestPercent(runs, context.Items.Count),
            Questions = context.Items.Select(item => new QuizQuestionDto
            {
                QuizItemId = item.Id,
                Type = item.Type.ToString(),
                Question = item.Question,
                Choices = QuizRules.ParseChoices(item.Type, item.Choices).ToList()
            }).ToList()
        };
    }

    public async Task<QuizAnswerResultDto> SubmitAnswerAsync(
        int childProfileId, int sessionId, SubmitQuizAnswerRequestDto request, CancellationToken ct = default)
    {
        var answer = request.Answer?.Trim();
        if (string.IsNullOrEmpty(answer) || answer.Length > ChildQuizPolicy.MaxAnswerLength)
        {
            throw new BadRequestException(ChildQuizPolicy.BlankAnswerMessage);
        }

        var preview = await LoadAsync(childProfileId, sessionId, ct);
        if (!preview.Items.Any(value => value.Id == request.QuizItemId))
        {
            throw new NotFoundException("Câu hỏi", request.QuizItemId);
        }

        await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            // Cùng khóa theo truyện với ChildReadingService để hai thiết bị không làm hỏng lịch sử.
            await _unitOfWork.AcquireTransactionLockAsync(preview.Session.StoryId, ct);
            var context = await LoadAsync(childProfileId, sessionId, ct);
            var item = context.Items.FirstOrDefault(value => value.Id == request.QuizItemId)
                       ?? throw new NotFoundException("Câu hỏi", request.QuizItemId);
            var isCorrect = QuizRules.IsCorrect(item.Type, item.CorrectAnswer, answer);

            var last = context.Attempts.LastOrDefault();
            var isDoubleTap = last != null
                              && last.ReadingSessionId == context.Session.Id
                              && last.QuizItemId == item.Id
                              && string.Equals(last.AnswerGiven, answer, StringComparison.Ordinal)
                              && DateTime.UtcNow - last.AnsweredAt < ChildQuizPolicy.DoubleTapWindow;
            IReadOnlyList<QuizAttempt> attempts = context.Attempts;
            var reward = RewardOutcome.None;
            if (!isDoubleTap)
            {
                // Câu này đã từng được trả lời đúng ở bất kỳ lần nào trước đó chưa (chống "cày" EXP).
                var firstTimeCorrect = isCorrect
                                       && !context.Attempts.Any(past => past.QuizItemId == item.Id && past.IsCorrect == true);
                var attempt = await _unitOfWork.Repository<QuizAttempt>().AddAsync(new QuizAttempt
                {
                    ReadingSessionId = context.Session.Id,
                    QuizItemId = item.Id,
                    AnswerGiven = answer,
                    IsCorrect = isCorrect,
                    AnsweredAt = DateTime.UtcNow
                }, ct);
                attempts = context.Attempts.Append(attempt).ToList();
                await AddTelemetryAsync(context.Session.Id, TelemetryEventType.QuizAnswered,
                    new { quizItemId = item.Id, isCorrect }, ct);

                var afterRuns = QuizRunCalculator.Split(attempts);
                var perfectRunJustCompleted = false;
                if (afterRuns.Count > 0 && afterRuns[^1].Answered == context.Items.Count)
                {
                    var finished = afterRuns[^1];
                    perfectRunJustCompleted = finished.Correct == context.Items.Count;
                    await AddTelemetryAsync(context.Session.Id, TelemetryEventType.QuizCompleted,
                        new
                        {
                            correct = finished.Correct,
                            total = context.Items.Count,
                            percent = QuizRunCalculator.Percent(finished.Correct, context.Items.Count)
                        }, ct);
                }

                reward = await _rewards.OnQuizAnsweredAsync(
                    context.Session, firstTimeCorrect, perfectRunJustCompleted, ct);
                await _unitOfWork.SaveChangesAsync(ct);
            }

            await _unitOfWork.CommitTransactionAsync(ct);

            var runs = QuizRunCalculator.Split(attempts);
            var current = runs.Count == 0 ? new QuizRun(0, 0) : runs[^1];
            return new QuizAnswerResultDto
            {
                QuizItemId = item.Id,
                IsCorrect = isDoubleTap ? last!.IsCorrect == true : isCorrect,
                Feedback = (isDoubleTap ? last!.IsCorrect == true : isCorrect)
                    ? ChildQuizPolicy.CorrectFeedback
                    : ChildQuizPolicy.NotMatchedFeedback,
                RunCompleted = context.Items.Count > 0 && current.Answered == context.Items.Count,
                CurrentPercent = QuizRunCalculator.Percent(current.Correct, context.Items.Count),
                BestPercent = QuizRunCalculator.BestPercent(runs, context.Items.Count),
                ExpGained = reward.ExpGained,
                NewBadges = reward.NewBadgeCodes.ToList()
            };
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<QuizResultDto> GetResultAsync(int childProfileId, int sessionId, CancellationToken ct = default)
    {
        var context = await LoadAsync(childProfileId, sessionId, ct);
        var runs = QuizRunCalculator.Split(context.Attempts);
        var current = runs.Count == 0 ? new QuizRun(0, 0) : runs[^1];
        var total = context.Items.Count;
        return new QuizResultDto
        {
            SessionId = context.Session.Id,
            TotalQuestions = total,
            Answered = current.Answered,
            Correct = current.Correct,
            CurrentPercent = QuizRunCalculator.Percent(current.Correct, total),
            BestPercent = QuizRunCalculator.BestPercent(runs, total),
            RunCompleted = total > 0 && current.Answered == total
        };
    }

    private sealed record QuizContext(ReadingSession Session, IReadOnlyList<QuizItem> Items, IReadOnlyList<QuizAttempt> Attempts);

    /// <summary>Kiểm tra phiên rồi nạp câu hỏi và toàn bộ lịch sử trả lời của trẻ cho đúng phiên bản truyện.</summary>
    private async Task<QuizContext> LoadAsync(int childProfileId, int sessionId, CancellationToken ct)
    {
        // Read without tracking so revalidation after the workflow lock sees committed interruptions.
        var session = await _unitOfWork.Repository<ReadingSession>().FirstOrDefaultAsync(
            value => value.Id == sessionId, cancellationToken: ct);
        if (session == null || session.IsDeleted || session.ChildProfileId != childProfileId)
        {
            throw new NotFoundException("Phiên đọc", sessionId);
        }

        if (session.ForceExitRequestedAt != null || session.Status == SessionStatus.ForceExited)
        {
            throw new ForbiddenException(ReadingSessionInterruptPolicy.ChildMessage);
        }

        if (session.Status != SessionStatus.Completed)
        {
            throw new ConflictException(ChildQuizPolicy.ReadFirstMessage);
        }

        var versionId = session.StoryVersionId ?? throw new ForbiddenException(ChildReadingPolicy.StoryUnavailableMessage);
        var items = (await _unitOfWork.Repository<QuizItem>().FindAsync(
                item => item.StoryVersionId == versionId && !item.IsDeleted, cancellationToken: ct))
            .OrderBy(item => item.Id).ToList();

        var storyId = session.StoryId;
        var sessionIds = (await _unitOfWork.Repository<ReadingSession>().FindAsync(
                other => other.ChildProfileId == childProfileId && other.StoryId == storyId && !other.IsDeleted,
                cancellationToken: ct))
            .Select(other => other.Id).ToList();
        var itemIds = items.Select(item => item.Id).ToList();
        var attempts = (await _unitOfWork.Repository<QuizAttempt>().FindAsync(
                attempt => sessionIds.Contains(attempt.ReadingSessionId)
                           && itemIds.Contains(attempt.QuizItemId)
                           && !attempt.IsDeleted,
                cancellationToken: ct))
            .OrderBy(attempt => attempt.Id).ToList();

        return new QuizContext(session, items, attempts);
    }

    private Task AddTelemetryAsync(int sessionId, TelemetryEventType type, object payload, CancellationToken ct) =>
        _unitOfWork.Repository<TelemetryLog>().AddAsync(new TelemetryLog
        {
            ReadingSessionId = sessionId,
            EventType = type,
            EventPayload = JsonSerializer.Serialize(payload),
            OccurredAt = DateTime.UtcNow
        }, ct);
}
