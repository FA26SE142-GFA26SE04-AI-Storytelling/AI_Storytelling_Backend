using Moq;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ChildRoadmaps;
using StoryPlatform.Application.Features.ChildRoadmaps.Interfaces;
using StoryPlatform.Application.Features.ReadingSessions;
using StoryPlatform.Application.Features.ReadingSessions.Interfaces;
using StoryPlatform.Application.Features.ReadingSessions.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ReadingSessions;

/// <summary>Remaining guard/rollback branches of ChildReadingService (Report 5, Việc 3.4).</summary>
public class ChildReadingServiceBranchTests
{
    private readonly ReadingHarness _h = new();
    private readonly Mock<IChildRoadmapProgressRecorder> _recorder = new();
    private readonly Mock<IChildRewardService> _rewards = new();
    private readonly ChildReadingService _sut;

    public ChildReadingServiceBranchTests()
    {
        var eligibility = new Mock<IChildRoadmapEligibility>();
        eligibility.Setup(e => e.EvaluateAsync(It.IsAny<ChildProfile>(), It.IsAny<Story?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChildRoadmapSkipReason?)null);
        _rewards.Setup(r => r.OnStoryCompletedAsync(It.IsAny<ReadingSession>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RewardOutcome.None);
        _sut = new ChildReadingService(_h.UnitOfWork.Object, eligibility.Object, _recorder.Object, _rewards.Object);
    }

    [Fact]
    public async Task CompleteAsync_RoadmapRecorderFails_RollsBackAndRethrows()
    {
        var session = _h.AddSession();
        _recorder.Setup(r => r.RecordSessionCompletedAsync(session.Id, false, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("recorder down"));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.CompleteAsync(ReadingHarness.Child, session.Id));

        Assert.Equal("recorder down", error.Message);
        _h.UnitOfWork.Verify(u => u.RollbackTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        _h.UnitOfWork.Verify(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CompleteAsync_ReplayWithMalformedCompletionReceipt_ReportsFirstRead()
    {
        var session = _h.AddSession(SessionStatus.Completed);
        _h.Telemetry.Add(new TelemetryLog
        {
            Id = 1, ReadingSessionId = session.Id, EventType = TelemetryEventType.SessionCompleted, EventPayload = "{not json"
        });

        var result = await _sut.CompleteAsync(ReadingHarness.Child, session.Id);

        Assert.False(result.IsRetake);
        _recorder.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CompleteAsync_ReplayWithNonObjectReceipt_ReportsFirstRead()
    {
        var session = _h.AddSession(SessionStatus.Completed);
        _h.Telemetry.Add(new TelemetryLog
        {
            Id = 1, ReadingSessionId = session.Id, EventType = TelemetryEventType.SessionCompleted, EventPayload = "[true]"
        });

        Assert.False((await _sut.CompleteAsync(ReadingHarness.Child, session.Id)).IsRetake);
    }

    [Fact]
    public async Task LogEventAsync_NullPayload_IsStoredWithoutPayload()
    {
        var session = _h.AddSession();

        await _sut.LogEventAsync(ReadingHarness.Child, session.Id, new ReadingEventRequestDto { EventType = "PageCompleted" });

        var log = Assert.Single(_h.Telemetry);
        Assert.Equal(TelemetryEventType.PageCompleted, log.EventType);
        Assert.Null(log.EventPayload);
    }

    [Fact]
    public async Task LogEventAsync_PayloadOfExactly2000_IsAccepted()
    {
        var session = _h.AddSession();

        await _sut.LogEventAsync(ReadingHarness.Child, session.Id,
            new ReadingEventRequestDto { EventType = "TtsCompleted", Payload = new string('x', ChildReadingPolicy.MaxEventPayloadLength) });

        Assert.Single(_h.Telemetry);
    }

    [Fact]
    public async Task LogEventAsync_PayloadOf2001_ThrowsBadRequest()
    {
        var session = _h.AddSession();

        var error = await Assert.ThrowsAsync<BadRequestException>(() => _sut.LogEventAsync(ReadingHarness.Child, session.Id,
            new ReadingEventRequestDto { EventType = "TtsCompleted", Payload = new string('x', ChildReadingPolicy.MaxEventPayloadLength + 1) }));

        Assert.Equal("Nội dung sự kiện quá dài.", error.Message);
        Assert.Empty(_h.Telemetry);
    }
}

/// <summary>Remaining guard/rollback branches of ChildQuizService (Report 5, Việc 3.4).</summary>
public class ChildQuizServiceBranchTests
{
    private readonly ReadingHarness _h = new();
    private readonly Mock<IChildRewardService> _rewards = new();
    private readonly ChildQuizService _sut;

    public ChildQuizServiceBranchTests()
    {
        _rewards.Setup(r => r.OnQuizAnsweredAsync(
                It.IsAny<ReadingSession>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RewardOutcome.None);
        _sut = new ChildQuizService(_h.UnitOfWork.Object, _rewards.Object);
        _h.Quizzes.Add(new QuizItem
        {
            Id = 1, StoryVersionId = ReadingHarness.Version, Type = QuizType.TrueFalse, Question = "Thỏ chia cà rốt.",
            CorrectAnswer = "true", Choices = "[\"Đúng\",\"Sai\"]"
        });
    }

    [Fact]
    public async Task SubmitAnswerAsync_NullAnswer_ThrowsBadRequest()
    {
        var session = _h.AddSession(SessionStatus.Completed);

        var error = await Assert.ThrowsAsync<BadRequestException>(() => _sut.SubmitAnswerAsync(ReadingHarness.Child, session.Id,
            new SubmitQuizAnswerRequestDto { QuizItemId = 1, Answer = null! }));

        Assert.Equal(ChildQuizPolicy.BlankAnswerMessage, error.Message);
    }

    [Fact]
    public async Task SubmitAnswerAsync_AnswerOf201Characters_ThrowsBadRequest()
    {
        var session = _h.AddSession(SessionStatus.Completed);

        var error = await Assert.ThrowsAsync<BadRequestException>(() => _sut.SubmitAnswerAsync(ReadingHarness.Child, session.Id,
            new SubmitQuizAnswerRequestDto { QuizItemId = 1, Answer = new string('a', ChildQuizPolicy.MaxAnswerLength + 1) }));

        Assert.Equal(ChildQuizPolicy.BlankAnswerMessage, error.Message);
        Assert.Empty(_h.Attempts);
    }

    [Fact]
    public async Task SubmitAnswerAsync_RewardServiceFails_RollsBackAndRethrows()
    {
        var session = _h.AddSession(SessionStatus.Completed);
        _rewards.Setup(r => r.OnQuizAnsweredAsync(
                It.IsAny<ReadingSession>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("rewards down"));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.SubmitAnswerAsync(ReadingHarness.Child, session.Id,
            new SubmitQuizAnswerRequestDto { QuizItemId = 1, Answer = "true" }));

        Assert.Equal("rewards down", error.Message);
        _h.UnitOfWork.Verify(u => u.RollbackTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        _h.UnitOfWork.Verify(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
