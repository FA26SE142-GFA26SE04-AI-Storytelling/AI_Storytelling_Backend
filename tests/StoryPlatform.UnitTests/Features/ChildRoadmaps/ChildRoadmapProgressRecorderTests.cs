using Moq;
using StoryPlatform.Application.Features.ChildRoadmaps.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ChildRoadmaps;

public class ChildRoadmapProgressRecorderTests
{
    private readonly ChildRoadmapHarness _h = new();
    private readonly ChildRoadmapProgressRecorder _sut;

    private const int Child = ChildRoadmapHarness.Child;

    public ChildRoadmapProgressRecorderTests()
    {
        var eligibility = new ChildRoadmapEligibility(_h.UnitOfWork.Object);
        _sut = new ChildRoadmapProgressRecorder(
            _h.UnitOfWork.Object,
            new ChildRoadmapAdvancer(_h.UnitOfWork.Object, eligibility),
            new ChildRoadmapNotifier(_h.UnitOfWork.Object, _h.Notifications.Object),
            _h.Audit.Object);
    }

    private ReadingSession Session(int storyId, SessionStatus status = SessionStatus.Completed)
    {
        var session = new ReadingSession
        {
            Id = 1,
            ChildProfileId = Child,
            StoryId = storyId,
            Status = status,
            StartedAt = DateTime.UtcNow.AddMinutes(-10),
            CompletedAt = status == SessionStatus.Completed ? DateTime.UtcNow : null
        };
        _h.Sessions.Add(session);
        return session;
    }

    private ChildRoadmapItem Item(int id) => _h.Items.Single(item => item.Id == id);

    [Fact]
    public async Task CompletedSession_CompletesAndLinksOpenStepThenOpensNext()
    {
        _h.AddItem(1, Child, 10, ChildRoadmapItemStatus.Open);
        _h.AddItem(2, Child, 11, ChildRoadmapItemStatus.Waiting);
        var session = Session(10);

        var result = await _sut.RecordSessionCompletedAsync(1, false);

        Assert.Equal(1, result!.Id);
        Assert.Equal(ChildRoadmapItemStatus.Completed, Item(1).Status);
        Assert.Equal(1, session.ChildRoadmapItemId);
        Assert.Equal(ChildRoadmapItemStatus.Open, Item(2).Status);
    }

    [Fact]
    public async Task OutOfTurnStory_CompletesWaitingStepWithoutChangingOpenStep()
    {
        _h.AddItem(1, Child, 10, ChildRoadmapItemStatus.Open);
        _h.AddItem(2, Child, 12, ChildRoadmapItemStatus.Waiting);
        Session(12);

        await _sut.RecordSessionCompletedAsync(1, false);

        Assert.Equal(ChildRoadmapItemStatus.Open, Item(1).Status);
        Assert.Equal(ChildRoadmapItemStatus.Completed, Item(2).Status);
    }

    [Fact]
    public async Task LastStep_NotifiesOwnerAndViewProgressSupervisors()
    {
        _h.AddItem(1, Child, 10, ChildRoadmapItemStatus.Open);
        _h.AddSupervisor(Child, ChildRoadmapHarness.SecondSupervisorId,
            SupervisorRole.AdditionalSupervisor, Permission.ViewProgress);
        Session(10);

        await _sut.RecordSessionCompletedAsync(1, false);

        _h.Notifications.Verify(n => n.CreateAsync(
            ChildRoadmapHarness.ParentId, NotificationType.RoadmapCompleted,
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        _h.Notifications.Verify(n => n.CreateAsync(
            ChildRoadmapHarness.SecondSupervisorId, NotificationType.RoadmapCompleted,
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HoldMode_CompletesCurrentButDoesNotOpenOrNotifyCompletion()
    {
        _h.AddHoldMode(Child);
        _h.AddItem(1, Child, 10, ChildRoadmapItemStatus.Open);
        _h.AddItem(2, Child, 11, ChildRoadmapItemStatus.Waiting);
        Session(10);

        await _sut.RecordSessionCompletedAsync(1, false);

        Assert.Equal(ChildRoadmapItemStatus.Completed, Item(1).Status);
        Assert.Equal(ChildRoadmapItemStatus.Waiting, Item(2).Status);
        _h.Notifications.Verify(n => n.CreateAsync(
            It.IsAny<int>(), NotificationType.RoadmapCompleted,
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RetakeSession_IsIgnored()
    {
        _h.AddItem(1, Child, 10, ChildRoadmapItemStatus.Open);
        var session = Session(10);

        Assert.Null(await _sut.RecordSessionCompletedAsync(1, true));
        Assert.Equal(ChildRoadmapItemStatus.Open, Item(1).Status);
        Assert.Null(session.ChildRoadmapItemId);
    }

    [Theory]
    [InlineData(SessionStatus.Started)]
    [InlineData(SessionStatus.Reading)]
    [InlineData(SessionStatus.Abandoned)]
    public async Task IncompleteSession_IsIgnored(SessionStatus status)
    {
        _h.AddItem(1, Child, 10, ChildRoadmapItemStatus.Open);
        Session(10, status);
        Assert.Null(await _sut.RecordSessionCompletedAsync(1, false));
    }

    [Fact]
    public async Task SuccessfulCompletion_WritesAuditAndCommits()
    {
        _h.AddItem(1, Child, 10, ChildRoadmapItemStatus.Open);
        Session(10);

        await _sut.RecordSessionCompletedAsync(1, false);

        _h.Audit.Verify(a => a.LogAsync(
            null, "ChildRoadmapStepCompleted", nameof(ChildRoadmapItem), 1,
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
        _h.UnitOfWork.Verify(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
