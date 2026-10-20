using System.Linq.Expressions;
using Moq;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ContentReports.Interfaces;
using StoryPlatform.Application.Features.ContentReports.Services;
using StoryPlatform.Application.Features.ReadingSessions.Interfaces;
using StoryPlatform.Application.Features.ReadingSessions.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ReadingSessions;

public class ReadingSessionInterruptServiceTests
{
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly List<ReadingSession> _sessions = new();
    private readonly ReadingSessionInterruptService _sut;

    private const int Story = 10;
    private const int Child = 100;

    public ReadingSessionInterruptServiceTests()
    {
        var repository = new Mock<IGenericRepository<ReadingSession>>();
        repository.Setup(repo => repo.GetByIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((object id, CancellationToken _) =>
                _sessions.FirstOrDefault(session => session.Id == (int)id));
        repository.Setup(repo => repo.FindAsync(
                It.IsAny<Expression<Func<ReadingSession, bool>>>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<ReadingSession, bool>> predicate, string? _, CancellationToken _) =>
                (IReadOnlyList<ReadingSession>)_sessions.Where(predicate.Compile()).ToList());
        _unitOfWork.Setup(unit => unit.Repository<ReadingSession>()).Returns(repository.Object);
        _sut = new ReadingSessionInterruptService(_unitOfWork.Object);
    }

    private ReadingSession Session(
        int id,
        SessionStatus status,
        int storyId = Story,
        int childId = Child,
        int? versionId = 50,
        DateTime? completedAt = null)
    {
        var session = new ReadingSession
        {
            Id = id,
            ChildProfileId = childId,
            StoryId = storyId,
            StoryVersionId = versionId,
            Status = status,
            StartedAt = DateTime.UtcNow.AddMinutes(-20),
            CompletedAt = completedAt
        };
        _sessions.Add(session);
        return session;
    }

    [Theory]
    [InlineData(SessionStatus.Started)]
    [InlineData(SessionStatus.Reading)]
    [InlineData(SessionStatus.Activity)]
    public async Task RequestInterrupt_MarksEveryOpenSessionWithoutChangingStatus(SessionStatus status)
    {
        var session = Session(1, status);

        Assert.Equal(1, await _sut.RequestStoryInterruptAsync(Story));

        Assert.NotNull(session.ForceExitRequestedAt);
        Assert.Equal(status, session.Status);
    }

    [Fact]
    public async Task RequestInterrupt_CoversAllVersionsAndIgnoresOtherStories()
    {
        var first = Session(1, SessionStatus.Reading, versionId: 50);
        var second = Session(2, SessionStatus.Reading, childId: 101, versionId: 51);
        var noVersion = Session(3, SessionStatus.Started, childId: 102, versionId: null);
        var other = Session(4, SessionStatus.Reading, storyId: 11);

        Assert.Equal(3, await _sut.RequestStoryInterruptAsync(Story));

        Assert.All(new[] { first, second, noVersion }, session => Assert.NotNull(session.ForceExitRequestedAt));
        Assert.Null(other.ForceExitRequestedAt);
    }

    [Fact]
    public async Task RequestInterrupt_OnlyMarksRecentlyCompletedSessions()
    {
        var recent = Session(1, SessionStatus.Completed, completedAt: DateTime.UtcNow.AddMinutes(-5));
        var old = Session(2, SessionStatus.Completed, completedAt: DateTime.UtcNow.AddHours(-3));
        var abandoned = Session(3, SessionStatus.Abandoned);

        Assert.Equal(1, await _sut.RequestStoryInterruptAsync(Story));

        Assert.NotNull(recent.ForceExitRequestedAt);
        Assert.Null(old.ForceExitRequestedAt);
        Assert.Null(abandoned.ForceExitRequestedAt);
    }

    [Fact]
    public async Task RequestInterrupt_IsIdempotentAndKeepsFirstTimestamp()
    {
        var session = Session(1, SessionStatus.Reading);
        Assert.Equal(1, await _sut.RequestStoryInterruptAsync(Story));
        var firstTimestamp = session.ForceExitRequestedAt;

        Assert.Equal(0, await _sut.RequestStoryInterruptAsync(Story));
        Assert.Equal(firstTimestamp, session.ForceExitRequestedAt);
    }

    [Fact]
    public async Task Check_NoRequestAllowsReadingToContinue()
    {
        Session(1, SessionStatus.Reading);

        var signal = await _sut.CheckAsync(Child, 1);

        Assert.False(signal.ShouldExit);
        Assert.Equal("Reading", signal.SessionStatus);
        Assert.Null(signal.Message);
    }

    [Theory]
    [InlineData(SessionStatus.Started)]
    [InlineData(SessionStatus.Reading)]
    public async Task Check_RequestedOpenSessionBecomesForceExited(SessionStatus status)
    {
        var session = Session(1, status);
        session.ForceExitRequestedAt = DateTime.UtcNow;

        var signal = await _sut.CheckAsync(Child, 1);

        Assert.True(signal.ShouldExit);
        Assert.Equal("JourneyMap", signal.ReturnTo);
        Assert.Equal("ForceExited", signal.SessionStatus);
        Assert.Equal(SessionStatus.ForceExited, session.Status);
        Assert.NotNull(session.CompletedAt);
        Assert.DoesNotContain("archive", signal.Message!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("an toàn", signal.Message!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(SessionStatus.Activity)]
    [InlineData(SessionStatus.Completed)]
    public async Task Check_PostReadSessionKeepsStatusButStillReturnsExitSignal(SessionStatus status)
    {
        DateTime? completedAt = status == SessionStatus.Completed
            ? DateTime.UtcNow.AddMinutes(-3)
            : null;
        var session = Session(1, status, completedAt: completedAt);
        session.ForceExitRequestedAt = DateTime.UtcNow;

        var signal = await _sut.CheckAsync(Child, 1);

        Assert.True(signal.ShouldExit);
        Assert.Equal(status, session.Status);
        Assert.Equal(status.ToString(), signal.SessionStatus);
        Assert.Equal(completedAt, session.CompletedAt);
    }

    [Fact]
    public async Task Check_OtherChildUnknownOrDeletedSessionIsNotFound()
    {
        Session(1, SessionStatus.Reading, childId: 101);
        Session(2, SessionStatus.Reading).IsDeleted = true;

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.CheckAsync(Child, 1));
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.CheckAsync(Child, 2));
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.CheckAsync(Child, 999));
    }

    private static StoryArchiveContext Context(ArchivedReason reason) =>
        new(new Story { Id = Story }, reason, 11, DateTime.UtcNow);

    [Fact]
    public async Task ArchiveHandler_OnlySafetyConcernRequestsInterrupt()
    {
        var service = new Mock<IReadingSessionInterruptService>();
        var handler = new ReadingSessionArchiveHandler(service.Object);

        await handler.HandleAsync(Context(ArchivedReason.Copyright));
        await handler.HandleAsync(Context(ArchivedReason.Inappropriate));
        await handler.HandleAsync(Context(ArchivedReason.SafetyConcern));

        service.Verify(interrupt => interrupt.RequestStoryInterruptAsync(
            Story, It.IsAny<CancellationToken>()), Times.Once);
    }
}
