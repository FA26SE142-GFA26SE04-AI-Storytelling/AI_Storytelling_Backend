using Moq;
using StoryPlatform.Application.Features.ChildRoadmaps.Interfaces;
using StoryPlatform.Application.Features.ChildRoadmaps.Services;
using StoryPlatform.Application.Features.ContentReports.Interfaces;
using StoryPlatform.Application.Features.ContentReports.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ChildRoadmaps;

public class ChildRoadmapArchiveHandlerTests
{
    private readonly ChildRoadmapHarness _h = new();
    private readonly Mock<IChildRoadmapAdvancer> _advancer = new();
    private readonly ChildRoadmapArchiveHandler _sut;

    private const int Child = ChildRoadmapHarness.Child;
    private const int OtherChild = ChildRoadmapHarness.OtherChild;
    private const int Moderator = ChildRoadmapHarness.ModeratorId;
    private const int ArchivedStory = 10;

    public ChildRoadmapArchiveHandlerTests()
    {
        _sut = new ChildRoadmapArchiveHandler(_h.UnitOfWork.Object, _h.Audit.Object, _advancer.Object);
    }

    private StoryArchiveContext Context(ArchivedReason reason = ArchivedReason.SafetyConcern)
    {
        var story = _h.Stories.Single(existing => existing.Id == ArchivedStory);
        story.Status = StoryStatus.Rejected;
        return new StoryArchiveContext(story, reason, Moderator, DateTime.UtcNow);
    }

    private ChildRoadmapItem Item(int id) => _h.Items.Single(item => item.Id == id);

    [Fact]
    public async Task HandleAsync_OpenStep_IsSkippedAsStoryUnavailableAndTheNextStepIsRequested()
    {
        _h.AddItem(1, Child, ArchivedStory, ChildRoadmapItemStatus.Open);
        _h.AddItem(2, Child, 11, ChildRoadmapItemStatus.Waiting);
        var before = DateTime.UtcNow;

        await _sut.HandleAsync(Context());

        Assert.Equal(ChildRoadmapItemStatus.Skipped, Item(1).Status);
        Assert.Equal(ChildRoadmapSkipReason.StoryUnavailable, Item(1).SkipReason);
        Assert.True(Item(1).SkippedAt >= before);
        _advancer.Verify(a => a.AdvanceAsync(Child, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WaitingStep_IsSkippedAndNoAdvanceIsNeeded()
    {
        _h.AddItem(1, Child, 11, ChildRoadmapItemStatus.Open);
        _h.AddItem(2, Child, ArchivedStory, ChildRoadmapItemStatus.Waiting);

        await _sut.HandleAsync(Context());

        Assert.Equal(ChildRoadmapItemStatus.Skipped, Item(2).Status);
        Assert.Equal(ChildRoadmapSkipReason.StoryUnavailable, Item(2).SkipReason);
        Assert.Equal(ChildRoadmapItemStatus.Open, Item(1).Status);
        _advancer.Verify(a => a.AdvanceAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(ArchivedReason.SafetyConcern)]
    [InlineData(ArchivedReason.Copyright)]
    [InlineData(ArchivedReason.Inappropriate)]
    [InlineData(ArchivedReason.SupervisorPreference)]
    public async Task HandleAsync_EveryArchiveReason_SkipsTheStep(ArchivedReason reason)
    {
        _h.AddItem(1, Child, ArchivedStory, ChildRoadmapItemStatus.Waiting);

        await _sut.HandleAsync(Context(reason));

        Assert.Equal(ChildRoadmapItemStatus.Skipped, Item(1).Status);
    }

    [Fact]
    public async Task HandleAsync_EveryChildWithTheStory_IsAffectedAndEachOpenOneIsAdvanced()
    {
        _h.AddItem(1, Child, ArchivedStory, ChildRoadmapItemStatus.Open);
        _h.AddItem(2, OtherChild, ArchivedStory, ChildRoadmapItemStatus.Open);

        await _sut.HandleAsync(Context());

        Assert.All(_h.Items, item => Assert.Equal(ChildRoadmapItemStatus.Skipped, item.Status));
        _advancer.Verify(a => a.AdvanceAsync(Child, It.IsAny<CancellationToken>()), Times.Once);
        _advancer.Verify(a => a.AdvanceAsync(OtherChild, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(ChildRoadmapItemStatus.Completed)]
    [InlineData(ChildRoadmapItemStatus.Skipped)]
    [InlineData(ChildRoadmapItemStatus.Removed)]
    public async Task HandleAsync_FinishedSteps_AreLeftAlone(ChildRoadmapItemStatus status)
    {
        var finished = _h.AddItem(1, Child, ArchivedStory, status);

        await _sut.HandleAsync(Context());

        Assert.Equal(status, finished.Status);
        Assert.Null(finished.SkipReason);
        _h.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_OtherStoriesInTheSameRoadmap_AreNotTouched()
    {
        var other = _h.AddItem(1, Child, 11, ChildRoadmapItemStatus.Open);
        _h.AddItem(2, Child, ArchivedStory, ChildRoadmapItemStatus.Waiting);

        await _sut.HandleAsync(Context());

        Assert.Equal(ChildRoadmapItemStatus.Open, other.Status);
        Assert.Null(other.SkipReason);
    }

    [Fact]
    public async Task HandleAsync_WritesAnAuditEntryPerSkippedStep()
    {
        _h.AddItem(1, Child, ArchivedStory, ChildRoadmapItemStatus.Open);
        _h.AddItem(2, OtherChild, ArchivedStory, ChildRoadmapItemStatus.Waiting);

        await _sut.HandleAsync(Context());

        foreach (var id in new[] { 1, 2 })
        {
            _h.Audit.Verify(a => a.LogAsync(
                Moderator, "ChildRoadmapItemSkippedByArchive", nameof(ChildRoadmapItem), id,
                It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Fact]
    public async Task HandleAsync_NoStepForTheStory_DoesNothing()
    {
        _h.AddItem(1, Child, 11, ChildRoadmapItemStatus.Open);

        await _sut.HandleAsync(Context());

        _h.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _advancer.Verify(a => a.AdvanceAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_EndToEndWithTheRealAdvancer_OpensTheNextStoryWhenTheOpenOneIsRetired()
    {
        _h.AddItem(1, Child, ArchivedStory, ChildRoadmapItemStatus.Open);
        _h.AddItem(2, Child, 11, ChildRoadmapItemStatus.Waiting);
        _h.AddItem(3, Child, 12, ChildRoadmapItemStatus.Waiting);
        var sut = new ChildRoadmapArchiveHandler(
            _h.UnitOfWork.Object, _h.Audit.Object,
            new ChildRoadmapAdvancer(_h.UnitOfWork.Object, new ChildRoadmapEligibility(_h.UnitOfWork.Object)));

        await sut.HandleAsync(Context());

        Assert.Equal(ChildRoadmapItemStatus.Skipped, Item(1).Status);
        Assert.Equal(ChildRoadmapItemStatus.Open, Item(2).Status);
        Assert.Equal(ChildRoadmapItemStatus.Waiting, Item(3).Status);
    }

    [Fact]
    public async Task HandleAsync_EndToEndWithTheRealAdvancer_HoldModeKeepsTheNextStepWaiting()
    {
        _h.AddHoldMode(Child);
        _h.AddItem(1, Child, ArchivedStory, ChildRoadmapItemStatus.Open);
        _h.AddItem(2, Child, 11, ChildRoadmapItemStatus.Waiting);
        var sut = new ChildRoadmapArchiveHandler(
            _h.UnitOfWork.Object, _h.Audit.Object,
            new ChildRoadmapAdvancer(_h.UnitOfWork.Object, new ChildRoadmapEligibility(_h.UnitOfWork.Object)));

        await sut.HandleAsync(Context());

        Assert.Equal(ChildRoadmapItemStatus.Skipped, Item(1).Status);
        Assert.Equal(ChildRoadmapItemStatus.Waiting, Item(2).Status);
    }
}
