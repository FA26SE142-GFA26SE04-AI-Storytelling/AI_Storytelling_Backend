using System.Text.Json;
using Moq;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ReadingSessions;
using StoryPlatform.Application.Features.ReadingSessions.Services;
using StoryPlatform.Application.Features.ReadingSessions.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ReadingSessions;

public class ChildQuizServiceTests
{
    private readonly ReadingHarness _h = new();
    private readonly ChildQuizService _sut;
    private readonly Mock<IChildRewardService> _rewards = new();

    public ChildQuizServiceTests()
    {
        _rewards.Setup(r => r.OnQuizAnsweredAsync(
                It.IsAny<ReadingSession>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RewardOutcome.None);
        _sut = new ChildQuizService(_h.UnitOfWork.Object, _rewards.Object);
    }

    private QuizItem AddQuiz(int id, QuizType type, string question, string correct, string? choices = null)
    {
        var item = new QuizItem
        {
            Id = id, StoryVersionId = ReadingHarness.Version, Type = type, Question = question,
            CorrectAnswer = correct, Choices = choices
        };
        _h.Quizzes.Add(item);
        return item;
    }

    private ReadingSession Finished() => _h.AddSession(SessionStatus.Completed);

    private void TwoQuestions()
    {
        AddQuiz(1, QuizType.MultipleChoice, "Nhân vật chính là ai?", "Chú Thỏ", "[\"Chú Thỏ\",\"Chú Rùa\"]");
        AddQuiz(2, QuizType.TrueFalse, "Thỏ chia cà rốt.", "true", "[\"Đúng\",\"Sai\"]");
    }

    // ---- GetQuizAsync ----

    [Fact]
    public async Task GetQuizAsync_ReturnsQuestionsAndChoices_WithoutAnyAnswer()
    {
        TwoQuestions();
        AddQuiz(3, QuizType.ShortAnswer, "Thỏ ra đâu?", "vườn");
        var session = Finished();

        var quiz = await _sut.GetQuizAsync(ReadingHarness.Child, session.Id);

        Assert.Equal(3, quiz.TotalQuestions);
        Assert.Equal(new[] { 1, 2, 3 }, quiz.Questions.Select(q => q.QuizItemId));
        Assert.Equal(new[] { "Chú Thỏ", "Chú Rùa" }, quiz.Questions[0].Choices);
        Assert.Equal(new[] { "Đúng", "Sai" }, quiz.Questions[1].Choices);
        Assert.Empty(quiz.Questions[2].Choices);
        Assert.Null(quiz.BestPercent);
        Assert.Null(typeof(QuizQuestionDto).GetProperty("CorrectAnswer"));
        Assert.Null(typeof(QuizDto).GetProperty("CorrectAnswer"));
    }

    [Fact]
    public async Task GetQuizAsync_ShowsBestPercentOfPastRuns()
    {
        TwoQuestions();
        var session = Finished();
        _h.Attempts.Add(new QuizAttempt { Id = 1, ReadingSessionId = session.Id, QuizItemId = 1, IsCorrect = true });
        _h.Attempts.Add(new QuizAttempt { Id = 2, ReadingSessionId = session.Id, QuizItemId = 2, IsCorrect = false });

        var quiz = await _sut.GetQuizAsync(ReadingHarness.Child, session.Id);

        Assert.Equal(50m, quiz.BestPercent);
    }

    [Fact]
    public async Task GetQuizAsync_StoryWithoutQuestions_ReturnsEmptyQuiz()
    {
        var session = Finished();

        var quiz = await _sut.GetQuizAsync(ReadingHarness.Child, session.Id);

        Assert.Equal(0, quiz.TotalQuestions);
        Assert.Empty(quiz.Questions);
    }

    [Theory]
    [InlineData(SessionStatus.Started)]
    [InlineData(SessionStatus.Reading)]
    [InlineData(SessionStatus.Activity)]
    [InlineData(SessionStatus.Abandoned)]
    public async Task GetQuizAsync_SessionNotFinished_IsConflict(SessionStatus status)
    {
        TwoQuestions();
        var session = _h.AddSession(status);

        var error = await Assert.ThrowsAsync<ConflictException>(() =>
            _sut.GetQuizAsync(ReadingHarness.Child, session.Id));

        Assert.Equal(ChildQuizPolicy.ReadFirstMessage, error.Message);
    }

    [Fact]
    public async Task GetQuizAsync_AnotherChildsSession_IsNotFound()
    {
        TwoQuestions();
        var session = _h.AddSession(SessionStatus.Completed, childId: ReadingHarness.OtherChild);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetQuizAsync(ReadingHarness.Child, session.Id));
    }

    [Fact]
    public async Task GetQuizAsync_InterruptedSession_IsForbiddenWithFriendlyMessage()
    {
        TwoQuestions();
        var session = Finished();
        session.ForceExitRequestedAt = DateTime.UtcNow;

        var error = await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.GetQuizAsync(ReadingHarness.Child, session.Id));

        Assert.Equal(ReadingSessionInterruptPolicy.ChildMessage, error.Message);
    }
    // ---- SubmitAnswerAsync ----

    private static SubmitQuizAnswerRequestDto Answer(int item, string? text) =>
        new() { QuizItemId = item, Answer = text };

    [Fact]
    public async Task SubmitAnswerAsync_CorrectAnswer_IsPraisedAndStored()
    {
        TwoQuestions();
        var session = Finished();

        var result = await _sut.SubmitAnswerAsync(ReadingHarness.Child, session.Id, Answer(1, "Chú Thỏ"));

        Assert.True(result.IsCorrect);
        Assert.Equal(ChildQuizPolicy.CorrectFeedback, result.Feedback);
        var stored = Assert.Single(_h.Attempts);
        Assert.Equal(session.Id, stored.ReadingSessionId);
        Assert.Equal(1, stored.QuizItemId);
        Assert.Equal("Chú Thỏ", stored.AnswerGiven);
        Assert.True(stored.IsCorrect);
        Assert.Contains(_h.Telemetry, t => t.EventType == TelemetryEventType.QuizAnswered);
    }

    [Fact]
    public async Task SubmitAnswerAsync_NotMatching_GivesFriendlyHint_WithoutRevealingAnswerOrSayingWrong()
    {
        TwoQuestions();
        var session = Finished();

        var result = await _sut.SubmitAnswerAsync(ReadingHarness.Child, session.Id, Answer(1, "Chú Rùa"));

        Assert.False(result.IsCorrect);
        Assert.Equal(ChildQuizPolicy.NotMatchedFeedback, result.Feedback);
        Assert.DoesNotContain("Chú Thỏ", result.Feedback);
        Assert.DoesNotContain("sai", result.Feedback, StringComparison.OrdinalIgnoreCase);
        Assert.False(_h.Attempts.Single().IsCorrect);
    }

    [Theory]
    [InlineData("Đúng")]
    [InlineData("true")]
    public async Task SubmitAnswerAsync_TrueFalse_AcceptsEitherVocabulary(string given)
    {
        TwoQuestions();
        var session = Finished();

        var result = await _sut.SubmitAnswerAsync(ReadingHarness.Child, session.Id, Answer(2, given));

        Assert.True(result.IsCorrect);
    }

    [Fact]
    public async Task SubmitAnswerAsync_LastQuestionOfARun_CompletesItOnce()
    {
        TwoQuestions();
        var session = Finished();

        var first = await _sut.SubmitAnswerAsync(ReadingHarness.Child, session.Id, Answer(1, "Chú Thỏ"));
        var last = await _sut.SubmitAnswerAsync(ReadingHarness.Child, session.Id, Answer(2, "Sai"));

        Assert.False(first.RunCompleted);
        Assert.True(last.RunCompleted);
        Assert.Equal(50m, last.CurrentPercent);
        Assert.Equal(50m, last.BestPercent);
        Assert.Single(_h.Telemetry, t => t.EventType == TelemetryEventType.QuizCompleted);
    }

    [Fact]
    public async Task SubmitAnswerAsync_Retake_KeepsTheBestRun()
    {
        TwoQuestions();
        var session = Finished();
        await _sut.SubmitAnswerAsync(ReadingHarness.Child, session.Id, Answer(1, "Chú Thỏ"));
        await _sut.SubmitAnswerAsync(ReadingHarness.Child, session.Id, Answer(2, "Sai"));         // lần 1: 50%
        DateOlder();
        await _sut.SubmitAnswerAsync(ReadingHarness.Child, session.Id, Answer(1, "Chú Thỏ"));
        var second = await _sut.SubmitAnswerAsync(ReadingHarness.Child, session.Id, Answer(2, "Đúng")); // lần 2: 100%
        DateOlder();
        await _sut.SubmitAnswerAsync(ReadingHarness.Child, session.Id, Answer(1, "Chú Rùa"));
        var third = await _sut.SubmitAnswerAsync(ReadingHarness.Child, session.Id, Answer(2, "Sai"));   // lần 3: 0%

        Assert.Equal(100m, second.CurrentPercent);
        Assert.Equal(0m, third.CurrentPercent);
        Assert.Equal(100m, third.BestPercent);
        Assert.Equal(3, _h.Telemetry.Count(t => t.EventType == TelemetryEventType.QuizCompleted));
    }

    [Fact]
    public async Task SubmitAnswerAsync_RetakeInANewSession_CountsTowardsTheSameBest()
    {
        TwoQuestions();
        var first = Finished();
        await _sut.SubmitAnswerAsync(ReadingHarness.Child, first.Id, Answer(1, "Chú Thỏ"));
        await _sut.SubmitAnswerAsync(ReadingHarness.Child, first.Id, Answer(2, "Đúng"));         // 100%
        var second = Finished();

        var quiz = await _sut.GetQuizAsync(ReadingHarness.Child, second.Id);

        Assert.Equal(100m, quiz.BestPercent);
    }

    [Fact]
    public async Task SubmitAnswerAsync_DoubleTap_IsStoredOnce()
    {
        TwoQuestions();
        var session = Finished();

        await _sut.SubmitAnswerAsync(ReadingHarness.Child, session.Id, Answer(1, "Chú Thỏ"));
        var again = await _sut.SubmitAnswerAsync(ReadingHarness.Child, session.Id, Answer(1, "Chú Thỏ"));

        Assert.Single(_h.Attempts);
        Assert.True(again.IsCorrect);
    }

    [Fact]
    public async Task SubmitAnswerAsync_BlankAnswer_IsBadRequestAndStoresNothing()
    {
        TwoQuestions();
        var session = Finished();

        var error = await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.SubmitAnswerAsync(ReadingHarness.Child, session.Id, Answer(1, "   ")));

        Assert.Equal(ChildQuizPolicy.BlankAnswerMessage, error.Message);
        Assert.Empty(_h.Attempts);
    }

    [Fact]
    public async Task SubmitAnswerAsync_TooLongAnswer_IsBadRequest()
    {
        TwoQuestions();
        var session = Finished();

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.SubmitAnswerAsync(ReadingHarness.Child, session.Id,
                Answer(1, new string('a', ChildQuizPolicy.MaxAnswerLength + 1))));
    }

    [Fact]
    public async Task SubmitAnswerAsync_QuestionOfAnotherStory_IsNotFound()
    {
        TwoQuestions();
        _h.Quizzes.Add(new QuizItem { Id = 99, StoryVersionId = 999, Type = QuizType.ShortAnswer, Question = "?", CorrectAnswer = "x" });
        var session = Finished();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _sut.SubmitAnswerAsync(ReadingHarness.Child, session.Id, Answer(99, "x")));
        Assert.Empty(_h.Attempts);
    }

    [Fact]
    public async Task SubmitAnswerAsync_SessionNotFinished_IsConflictAndStoresNothing()
    {
        TwoQuestions();
        var session = _h.AddSession(SessionStatus.Reading);

        await Assert.ThrowsAsync<ConflictException>(() =>
            _sut.SubmitAnswerAsync(ReadingHarness.Child, session.Id, Answer(1, "Chú Thỏ")));
        Assert.Empty(_h.Attempts);
    }

    [Fact]
    public async Task SubmitAnswerAsync_RollsBackWhenSavingFails()
    {
        TwoQuestions();
        var session = Finished();
        _h.UnitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.SubmitAnswerAsync(ReadingHarness.Child, session.Id, Answer(1, "Chú Thỏ")));

        _h.UnitOfWork.Verify(u => u.RollbackTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---- GetResultAsync ----

    [Fact]
    public async Task GetResultAsync_BeforeAnyAnswer_IsAllZero()
    {
        TwoQuestions();
        var session = Finished();

        var result = await _sut.GetResultAsync(ReadingHarness.Child, session.Id);

        Assert.Equal(2, result.TotalQuestions);
        Assert.Equal(0, result.Answered);
        Assert.Equal(0, result.Correct);
        Assert.Equal(0m, result.CurrentPercent);
        Assert.Null(result.BestPercent);
        Assert.False(result.RunCompleted);
    }

    [Fact]
    public async Task GetResultAsync_AfterPartialRun_CountsSkippedAsNotCorrect()
    {
        TwoQuestions();
        var session = Finished();
        await _sut.SubmitAnswerAsync(ReadingHarness.Child, session.Id, Answer(1, "Chú Thỏ"));

        var result = await _sut.GetResultAsync(ReadingHarness.Child, session.Id);

        Assert.Equal(1, result.Answered);
        Assert.Equal(1, result.Correct);
        Assert.Equal(50m, result.CurrentPercent);
        Assert.False(result.RunCompleted);
    }

    [Fact]
    public async Task GetResultAsync_AnotherChildsSession_IsNotFound()
    {
        TwoQuestions();
        var session = _h.AddSession(SessionStatus.Completed, childId: ReadingHarness.OtherChild);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetResultAsync(ReadingHarness.Child, session.Id));
    }

    /// <summary>Đẩy mọi lần nộp cũ lùi xa để không bị coi là bấm đúp.</summary>
    private void DateOlder()
    {
        foreach (var attempt in _h.Attempts)
        {
            attempt.AnsweredAt = DateTime.UtcNow.AddMinutes(-5);
        }
    }

    [Fact]
    public async Task SubmitAnswerAsync_InterruptionWhileWaitingForLock_ReadsFreshSessionAndRollsBack()
    {
        TwoQuestions();
        var session = Finished();
        // Simulate the stale entity EF FindAsync could return from its change tracker.
        var cached = new ReadingSession
        {
            Id = session.Id, ChildProfileId = session.ChildProfileId, StoryId = session.StoryId,
            StoryVersionId = session.StoryVersionId, Status = SessionStatus.Completed
        };
        Mock.Get(_h.UnitOfWork.Object.Repository<ReadingSession>())
            .Setup(r => r.GetByIdAsync(session.Id, It.IsAny<CancellationToken>())).ReturnsAsync(cached);
        _h.UnitOfWork.Setup(u => u.AcquireTransactionLockAsync(ReadingHarness.Story, It.IsAny<CancellationToken>()))
            .Callback(() => session.ForceExitRequestedAt = DateTime.UtcNow).Returns(Task.CompletedTask);

        var error = await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.SubmitAnswerAsync(ReadingHarness.Child, session.Id, Answer(1, "Chú Thỏ")));

        Assert.Equal(ReadingSessionInterruptPolicy.ChildMessage, error.Message);
        Assert.Empty(_h.Attempts);
        Assert.Empty(_h.Telemetry);
        _h.UnitOfWork.Verify(u => u.RollbackTransactionAsync(CancellationToken.None), Times.Once);
        _h.UnitOfWork.Verify(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SubmitAnswerAsync_QuestionDeletedWhileWaitingForLock_StoresNothing()
    {
        TwoQuestions();
        var session = Finished();
        _h.UnitOfWork.Setup(u => u.AcquireTransactionLockAsync(ReadingHarness.Story, It.IsAny<CancellationToken>()))
            .Callback(() => _h.Quizzes[0].IsDeleted = true).Returns(Task.CompletedTask);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _sut.SubmitAnswerAsync(ReadingHarness.Child, session.Id, Answer(1, "Chú Thỏ")));

        Assert.Empty(_h.Attempts);
        Assert.Empty(_h.Telemetry);
        _h.UnitOfWork.Verify(u => u.RollbackTransactionAsync(CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task SubmitAnswerAsync_QuestionChangedWhileWaitingForLock_UsesFreshKey()
    {
        TwoQuestions();
        var session = Finished();
        _h.UnitOfWork.Setup(u => u.AcquireTransactionLockAsync(ReadingHarness.Story, It.IsAny<CancellationToken>()))
            .Callback(() => _h.Quizzes[0] = new QuizItem
            {
                Id = 1, StoryVersionId = ReadingHarness.Version, Type = QuizType.MultipleChoice,
                Question = "Nhân vật chính là ai?", CorrectAnswer = "Chú Rùa"
            }).Returns(Task.CompletedTask);

        var result = await _sut.SubmitAnswerAsync(ReadingHarness.Child, session.Id, Answer(1, "Chú Rùa"));

        Assert.True(result.IsCorrect);
        Assert.True(Assert.Single(_h.Attempts).IsCorrect);
    }

    [Fact]
    public async Task SubmitAnswerAsync_SameAnswerInNewSession_IsNotADoubleTap()
    {
        AddQuiz(1, QuizType.TrueFalse, "Thỏ chia cà rốt.", "true");
        var first = Finished();
        await _sut.SubmitAnswerAsync(ReadingHarness.Child, first.Id, Answer(1, "Đúng"));
        var second = Finished();

        var result = await _sut.SubmitAnswerAsync(ReadingHarness.Child, second.Id, Answer(1, "Đúng"));

        Assert.Equal(2, _h.Attempts.Count);
        Assert.Equal(second.Id, _h.Attempts.Last().ReadingSessionId);
        Assert.True(result.RunCompleted);
        Assert.Equal(2, _h.Telemetry.Count(t => t.EventType == TelemetryEventType.QuizCompleted));
    }

    [Fact]
    public async Task SubmitAnswerAsync_FinalAnswerDoubleTap_EmitsCompletionOnlyOnceWithScore()
    {
        TwoQuestions();
        var session = Finished();
        await _sut.SubmitAnswerAsync(ReadingHarness.Child, session.Id, Answer(1, "Chú Thỏ"));
        await _sut.SubmitAnswerAsync(ReadingHarness.Child, session.Id, Answer(2, "Sai"));

        var retry = await _sut.SubmitAnswerAsync(ReadingHarness.Child, session.Id, Answer(2, " Sai "));

        Assert.True(retry.RunCompleted);
        Assert.False(retry.IsCorrect);
        Assert.Equal(50m, retry.CurrentPercent);
        Assert.Equal(2, _h.Attempts.Count);
        Assert.Equal(2, _h.Telemetry.Count(t => t.EventType == TelemetryEventType.QuizAnswered));
        var completed = Assert.Single(_h.Telemetry, t => t.EventType == TelemetryEventType.QuizCompleted);
        using var payload = JsonDocument.Parse(completed.EventPayload!);
        Assert.Equal(1, payload.RootElement.GetProperty("correct").GetInt32());
        Assert.Equal(2, payload.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(50m, payload.RootElement.GetProperty("percent").GetDecimal());
    }

    [Fact]
    public async Task SubmitAnswerAsync_SameAnswerAfterDoubleTapWindow_StartsAnotherRun()
    {
        TwoQuestions();
        var session = Finished();
        await _sut.SubmitAnswerAsync(ReadingHarness.Child, session.Id, Answer(1, "Chú Thỏ"));
        DateOlder();

        await _sut.SubmitAnswerAsync(ReadingHarness.Child, session.Id, Answer(1, "Chú Thỏ"));

        Assert.Equal(2, _h.Attempts.Count);
        Assert.Equal(2, QuizRunCalculator.Split(_h.Attempts).Count);
    }

    [Fact]
    public async Task GetResultAsync_FiltersOtherChildrenStoriesVersionsAndDeletedHistory_AndOrdersById()
    {
        TwoQuestions();
        var session = Finished();
        var otherChild = _h.AddSession(SessionStatus.Completed, childId: ReadingHarness.OtherChild);
        var otherStory = _h.AddSession(SessionStatus.Completed, storyId: 99);
        var deletedSession = Finished();
        deletedSession.IsDeleted = true;
        AddQuiz(3, QuizType.ShortAnswer, "?", "x").StoryVersionId = 999;
        AddQuiz(4, QuizType.ShortAnswer, "?", "x").IsDeleted = true;
        _h.Attempts.AddRange(new[]
        {
            new QuizAttempt { Id = 2, ReadingSessionId = session.Id, QuizItemId = 1, IsCorrect = true },
            new QuizAttempt { Id = 1, ReadingSessionId = session.Id, QuizItemId = 1, IsCorrect = false },
            new QuizAttempt { Id = 3, ReadingSessionId = otherChild.Id, QuizItemId = 2, IsCorrect = true },
            new QuizAttempt { Id = 4, ReadingSessionId = otherStory.Id, QuizItemId = 2, IsCorrect = true },
            new QuizAttempt { Id = 5, ReadingSessionId = deletedSession.Id, QuizItemId = 2, IsCorrect = true },
            new QuizAttempt { Id = 6, ReadingSessionId = session.Id, QuizItemId = 2, IsCorrect = true, IsDeleted = true },
            new QuizAttempt { Id = 7, ReadingSessionId = session.Id, QuizItemId = 3, IsCorrect = true },
            new QuizAttempt { Id = 8, ReadingSessionId = session.Id, QuizItemId = 4, IsCorrect = true }
        });

        var result = await _sut.GetResultAsync(ReadingHarness.Child, session.Id);
        var quiz = await _sut.GetQuizAsync(ReadingHarness.Child, session.Id);

        Assert.Equal(new[] { 1, 2 }, quiz.Questions.Select(q => q.QuizItemId));
        Assert.Equal(2, result.TotalQuestions);
        Assert.Equal(1, result.Answered);
        Assert.Equal(1, result.Correct);
        Assert.Equal(50m, result.CurrentPercent);
        Assert.Equal(50m, result.BestPercent);
        Assert.False(result.RunCompleted);
        Assert.Empty(_h.Telemetry);
    }

    [Fact]
    public async Task AllOperations_ForceExitedSession_AreForbiddenWithoutWrites()
    {
        TwoQuestions();
        var session = _h.AddSession(SessionStatus.ForceExited);

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.GetQuizAsync(ReadingHarness.Child, session.Id));
        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.GetResultAsync(ReadingHarness.Child, session.Id));
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.SubmitAnswerAsync(ReadingHarness.Child, session.Id, Answer(1, "Chú Thỏ")));

        Assert.Empty(_h.Attempts);
        Assert.Empty(_h.Telemetry);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AllOperations_MissingOrDeletedSession_AreNotFound(bool deleted)
    {
        TwoQuestions();
        var sessionId = deleted ? Finished().Id : 999;
        if (deleted) _h.Sessions.Single().IsDeleted = true;

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetQuizAsync(ReadingHarness.Child, sessionId));
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetResultAsync(ReadingHarness.Child, sessionId));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _sut.SubmitAnswerAsync(ReadingHarness.Child, sessionId, Answer(1, "Chú Thỏ")));

        Assert.Empty(_h.Attempts);
        Assert.Empty(_h.Telemetry);
    }

    [Fact]
    public async Task GetQuizAsync_MissingVersion_IsForbidden()
    {
        var session = Finished();
        session.StoryVersionId = null;

        var error = await Assert.ThrowsAsync<ForbiddenException>(() => _sut.GetQuizAsync(ReadingHarness.Child, session.Id));

        Assert.Equal(ChildReadingPolicy.StoryUnavailableMessage, error.Message);
    }

    [Fact]
    public async Task GetResultAsync_NoQuestions_IsNotCompleted()
    {
        var result = await _sut.GetResultAsync(ReadingHarness.Child, Finished().Id);

        Assert.Equal(0, result.TotalQuestions);
        Assert.Equal(0m, result.CurrentPercent);
        Assert.Null(result.BestPercent);
        Assert.False(result.RunCompleted);
    }
    // ---- Thưởng khi nộp câu ----

    [Fact]
    public async Task SubmitAnswerAsync_FirstCorrect_AsksForExp_AndReportsIt()
    {
        TwoQuestions();
        var session = Finished();
        _rewards.Setup(r => r.OnQuizAnsweredAsync(session, true, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RewardOutcome(2, Array.Empty<string>()));

        var result = await _sut.SubmitAnswerAsync(ReadingHarness.Child, session.Id, Answer(1, "Chú Thỏ"));

        Assert.Equal(2, result.ExpGained);
        _rewards.Verify(r => r.OnQuizAnsweredAsync(session, true, false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SubmitAnswerAsync_NotMatching_AsksForNothingToReward()
    {
        TwoQuestions();
        var session = Finished();

        await _sut.SubmitAnswerAsync(ReadingHarness.Child, session.Id, Answer(1, "Chú Rùa"));

        _rewards.Verify(r => r.OnQuizAnsweredAsync(session, false, false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SubmitAnswerAsync_QuestionAlreadyAnsweredCorrectlyBefore_IsNotFirstTimeAgain()
    {
        TwoQuestions();
        var session = Finished();
        _h.Attempts.Add(new QuizAttempt
        {
            Id = 1, ReadingSessionId = session.Id, QuizItemId = 1, IsCorrect = true, AnsweredAt = DateTime.UtcNow.AddMinutes(-10)
        });

        await _sut.SubmitAnswerAsync(ReadingHarness.Child, session.Id, Answer(1, "Chú Thỏ"));

        _rewards.Verify(r => r.OnQuizAnsweredAsync(session, false, false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SubmitAnswerAsync_PerfectRun_AsksForTheChampionBadge()
    {
        TwoQuestions();
        var session = Finished();

        await _sut.SubmitAnswerAsync(ReadingHarness.Child, session.Id, Answer(1, "Chú Thỏ"));
        await _sut.SubmitAnswerAsync(ReadingHarness.Child, session.Id, Answer(2, "Đúng"));

        _rewards.Verify(r => r.OnQuizAnsweredAsync(session, true, true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SubmitAnswerAsync_ARunWithAMiss_IsNotPerfect()
    {
        TwoQuestions();
        var session = Finished();

        await _sut.SubmitAnswerAsync(ReadingHarness.Child, session.Id, Answer(1, "Chú Thỏ"));
        await _sut.SubmitAnswerAsync(ReadingHarness.Child, session.Id, Answer(2, "Sai"));

        _rewards.Verify(r => r.OnQuizAnsweredAsync(
            It.IsAny<ReadingSession>(), It.IsAny<bool>(), true, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SubmitAnswerAsync_DoubleTap_DoesNotRewardTwice()
    {
        TwoQuestions();
        var session = Finished();

        await _sut.SubmitAnswerAsync(ReadingHarness.Child, session.Id, Answer(1, "Chú Thỏ"));
        await _sut.SubmitAnswerAsync(ReadingHarness.Child, session.Id, Answer(1, "Chú Thỏ"));

        _rewards.Verify(r => r.OnQuizAnsweredAsync(
            It.IsAny<ReadingSession>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SubmitAnswerAsync_RewardFailure_RollsBack()
    {
        TwoQuestions();
        var session = Finished();
        _rewards.Setup(r => r.OnQuizAnsweredAsync(
                It.IsAny<ReadingSession>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("reward"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.SubmitAnswerAsync(ReadingHarness.Child, session.Id, Answer(1, "Chú Thỏ")));

        _h.UnitOfWork.Verify(u => u.RollbackTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
