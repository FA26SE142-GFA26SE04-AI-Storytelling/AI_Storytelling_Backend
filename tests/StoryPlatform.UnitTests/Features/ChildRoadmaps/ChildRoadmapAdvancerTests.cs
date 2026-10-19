using Moq;
using StoryPlatform.Application.Features.ChildRoadmaps.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ChildRoadmaps;

public class ChildRoadmapAdvancerTests
{
    private readonly ChildRoadmapHarness _h = new();
    private readonly ChildRoadmapAdvancer _sut;

    private const int Child = ChildRoadmapHarness.Child;

    public ChildRoadmapAdvancerTests()
    {
        _sut = new ChildRoadmapAdvancer(_h.UnitOfWork.Object);
    }

    private ChildRoadmapItem Item(int id) => _h.Items.Single(item => item.Id == id);

    [Fact]
    public async Task AdvanceAsync_OpensTheFirstWaitingStepByPosition()
    {
        _h.AddItem(1, Child, 10, ChildRoadmapItemStatus.Waiting, position: 2);
        _h.AddItem(2, Child, 11, ChildRoadmapItemStatus.Waiting, position: 1);
        var before = DateTime.UtcNow;

        var opened = await _sut.AdvanceAsync(Child);

        Assert.Equal(2, opened!.Id);
        Assert.Equal(ChildRoadmapItemStatus.Open, Item(2).Status);
        Assert.True(Item(2).OpenedAt >= before);
        Assert.Equal(ChildRoadmapItemStatus.Waiting, Item(1).Status);
    }

    [Fact]
    public async Task AdvanceAsync_ExpectedDays_SetsTheExpectedCompletionDate()
    {
        _h.AddItem(1, Child, 10, ChildRoadmapItemStatus.Waiting, expectedDays: 3);
        var before = DateTime.UtcNow;

        await _sut.AdvanceAsync(Child);

        Assert.InRange(Item(1).ExpectedCompletionAt!.Value, before.AddDays(3), DateTime.UtcNow.AddDays(3));
    }

    [Fact]
    public async Task AdvanceAsync_WithoutExpectedDays_LeavesNoDate()
    {
        _h.AddItem(1, Child, 10, ChildRoadmapItemStatus.Waiting);

        await _sut.AdvanceAsync(Child);

        Assert.Null(Item(1).ExpectedCompletionAt);
    }

    [Fact]
    public async Task AdvanceAsync_AStepIsAlreadyOpen_ChangesNothing()
    {
        var open = _h.AddItem(1, Child, 10, ChildRoadmapItemStatus.Open);
        var waiting = _h.AddItem(2, Child, 11, ChildRoadmapItemStatus.Waiting);

        var result = await _sut.AdvanceAsync(Child);

        Assert.Equal(open.Id, result!.Id);
        Assert.Equal(ChildRoadmapItemStatus.Waiting, waiting.Status);
        _h.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AdvanceAsync_NoWaitingStep_ReturnsNull()
    {
        _h.AddItem(1, Child, 10, ChildRoadmapItemStatus.Completed);

        Assert.Null(await _sut.AdvanceAsync(Child));
    }

    // ---- bước truyện không còn đọc được bị bỏ qua, xét bước kế ----

    [Theory]
    [InlineData("archived-status")]
    [InlineData("archived-at")]
    [InlineData("not-ready")]
    public async Task AdvanceAsync_UnavailableStory_IsSkippedAsStoryUnavailableAndTheNextOpens(string problem)
    {
        var broken = _h.Stories.Single(story => story.Id == 10);
        switch (problem)
        {
            case "archived-status": broken.Status = StoryStatus.Archived; break;
            case "archived-at": broken.ArchivedAt = DateTime.UtcNow; break;
            case "not-ready": broken.Status = StoryStatus.MediaProcessing; break;
        }

        _h.AddItem(1, Child, 10, ChildRoadmapItemStatus.Waiting);
        _h.AddItem(2, Child, 11, ChildRoadmapItemStatus.Waiting);
        var before = DateTime.UtcNow;

        var opened = await _sut.AdvanceAsync(Child);

        Assert.Equal(2, opened!.Id);
        Assert.Equal(ChildRoadmapItemStatus.Skipped, Item(1).Status);
        Assert.Equal(ChildRoadmapSkipReason.StoryUnavailable, Item(1).SkipReason);
        Assert.True(Item(1).SkippedAt >= before);
    }

    [Theory]
    [InlineData("deleted")]
    [InlineData("missing")]
    public async Task AdvanceAsync_DeletedOrMissingSource_IsSkippedAsSourceDeleted(string problem)
    {
        var storyId = 10;
        if (problem == "deleted")
        {
            _h.Stories.Single(story => story.Id == 10).IsDeleted = true;
        }
        else
        {
            storyId = 999;
        }

        _h.AddItem(1, Child, storyId, ChildRoadmapItemStatus.Waiting);
        _h.AddItem(2, Child, 11, ChildRoadmapItemStatus.Waiting);

        await _sut.AdvanceAsync(Child);

        Assert.Equal(ChildRoadmapSkipReason.SourceDeleted, Item(1).SkipReason);
        Assert.Equal(ChildRoadmapItemStatus.Open, Item(2).Status);
    }

    [Fact]
    public async Task AdvanceAsync_EveryWaitingStepUnavailable_SkipsThemAllAndOpensNothing()
    {
        _h.Stories.Single(story => story.Id == 10).Status = StoryStatus.Archived;
        _h.Stories.Single(story => story.Id == 11).Status = StoryStatus.Archived;
        _h.AddItem(1, Child, 10, ChildRoadmapItemStatus.Waiting);
        _h.AddItem(2, Child, 11, ChildRoadmapItemStatus.Waiting);

        var result = await _sut.AdvanceAsync(Child);

        Assert.Null(result);
        Assert.All(_h.Items, item => Assert.Equal(ChildRoadmapItemStatus.Skipped, item.Status));
    }

    // ---- hồ sơ và Hold Mode ----

    [Theory]
    [InlineData(ChildProfileStatus.Suspended)]
    [InlineData(ChildProfileStatus.Archived)]
    [InlineData(ChildProfileStatus.Deleted)]
    [InlineData(ChildProfileStatus.Draft)]
    public async Task AdvanceAsync_ChildNotActive_OpensNothing(ChildProfileStatus status)
    {
        _h.Children.Single(child => child.Id == Child).Status = status;
        var waiting = _h.AddItem(1, Child, 10, ChildRoadmapItemStatus.Waiting);

        var result = await _sut.AdvanceAsync(Child);

        Assert.Null(result);
        Assert.Equal(ChildRoadmapItemStatus.Waiting, waiting.Status);
    }

    [Theory]
    [InlineData(InterventionStatus.OpenHoldMode)]
    [InlineData(InterventionStatus.UnderReview)]
    public async Task AdvanceAsync_HoldModeActive_KeepsTheStepWaiting(InterventionStatus status)
    {
        _h.AddHoldMode(Child, status);
        var waiting = _h.AddItem(1, Child, 10, ChildRoadmapItemStatus.Waiting);

        var result = await _sut.AdvanceAsync(Child);

        Assert.Null(result);
        Assert.Equal(ChildRoadmapItemStatus.Waiting, waiting.Status);
    }

    [Fact]
    public async Task AdvanceAsync_HoldModeResolved_OpensNormally()
    {
        _h.AddHoldMode(Child, InterventionStatus.ResolvedUnlocked);
        _h.AddItem(1, Child, 10, ChildRoadmapItemStatus.Waiting);

        var result = await _sut.AdvanceAsync(Child);

        Assert.Equal(1, result!.Id);
    }

    [Fact]
    public async Task AdvanceAsync_HoldModeOfAnotherChild_DoesNotBlock()
    {
        _h.AddHoldMode(ChildRoadmapHarness.OtherChild);
        _h.AddItem(1, Child, 10, ChildRoadmapItemStatus.Waiting);

        Assert.NotNull(await _sut.AdvanceAsync(Child));
    }

    [Fact]
    public async Task AdvanceAsync_OnlyTouchesTheGivenChild()
    {
        var other = _h.AddItem(1, ChildRoadmapHarness.OtherChild, 10, ChildRoadmapItemStatus.Waiting);
        _h.AddItem(2, Child, 11, ChildRoadmapItemStatus.Waiting);

        await _sut.AdvanceAsync(Child);

        Assert.Equal(ChildRoadmapItemStatus.Waiting, other.Status);
    }

    [Fact]
    public async Task AdvanceAsync_UnknownChild_ReturnsNull()
    {
        Assert.Null(await _sut.AdvanceAsync(999));
    }
}
