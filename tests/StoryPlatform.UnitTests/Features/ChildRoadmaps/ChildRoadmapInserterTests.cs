using Moq;
using StoryPlatform.Application.Features.ChildRoadmaps;
using StoryPlatform.Application.Features.ChildRoadmaps.Interfaces;
using StoryPlatform.Application.Features.ChildRoadmaps.Services;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ChildRoadmaps;

public class ChildRoadmapInserterTests
{
    private readonly ChildRoadmapHarness _h = new();
    private readonly ChildRoadmapInserter _sut;

    public ChildRoadmapInserterTests()
    {
        _sut = new ChildRoadmapInserter(_h.UnitOfWork.Object);
    }

    private static NewRoadmapStep Step(int storyId, int? days = null, string? note = null) =>
        new(storyId, days, note);

    private Task<List<InsertedStep>> InsertAsync(
        RoadmapInsertPosition position, bool moveExisting, params NewRoadmapStep[] steps) =>
        _sut.InsertAsync(new InsertRequest(
            ChildRoadmapHarness.Child, ChildRoadmapHarness.ParentId, steps, position,
            ChildRoadmapItemSource.Manual, null, moveExisting));

    private int[] WaitingStories() => _h.Items
        .Where(item => item.ChildProfileId == ChildRoadmapHarness.Child
                       && item.Status == ChildRoadmapItemStatus.Waiting)
        .OrderBy(item => item.Position).Select(item => item.StoryId).ToArray();

    [Fact]
    public async Task End_AppendsAndKeepsPositionsContinuous()
    {
        _h.AddItem(1, ChildRoadmapHarness.Child, 10, ChildRoadmapItemStatus.Waiting);

        var result = await InsertAsync(RoadmapInsertPosition.End, false, Step(11), Step(12));

        Assert.Equal(new[] { 10, 11, 12 }, WaitingStories());
        Assert.Equal(new[] { 1, 2, 3 }, _h.Items.OrderBy(item => item.Position).Select(item => item.Position));
        Assert.All(result, step => Assert.True(step.Created));
    }

    [Fact]
    public async Task NextUp_PutsNewStepsAtTheFrontInInputOrder()
    {
        _h.AddItem(1, ChildRoadmapHarness.Child, 10, ChildRoadmapItemStatus.Waiting);

        await InsertAsync(RoadmapInsertPosition.NextUp, false, Step(11), Step(12));

        Assert.Equal(new[] { 11, 12, 10 }, WaitingStories());
    }

    [Fact]
    public async Task StoresMetadataAndTrimsNote()
    {
        await InsertAsync(RoadmapInsertPosition.End, false, Step(10, 3, "  Đọc nhé  "));

        var item = Assert.Single(_h.Items);
        Assert.Equal(3, item.ExpectedDays);
        Assert.Equal("Đọc nhé", item.Note);
        Assert.Equal(ChildRoadmapItemSource.Manual, item.Source);
    }

    [Fact]
    public async Task WaitingStoryWithNextUp_MovesWithoutCreatingDuplicate()
    {
        _h.AddItem(1, ChildRoadmapHarness.Child, 10, ChildRoadmapItemStatus.Waiting);
        _h.AddItem(2, ChildRoadmapHarness.Child, 11, ChildRoadmapItemStatus.Waiting);
        _h.AddItem(3, ChildRoadmapHarness.Child, 12, ChildRoadmapItemStatus.Waiting);

        var result = await InsertAsync(RoadmapInsertPosition.NextUp, true, Step(12));

        Assert.Equal(new[] { 12, 10, 11 }, WaitingStories());
        Assert.Equal(3, _h.Items.Count);
        Assert.True(result.Single().Moved);
        Assert.False(result.Single().Created);
    }

    [Fact]
    public async Task OpenStory_IsNeverMovedOrDuplicated()
    {
        _h.AddItem(1, ChildRoadmapHarness.Child, 10, ChildRoadmapItemStatus.Open);

        var result = await InsertAsync(RoadmapInsertPosition.NextUp, true, Step(10));

        Assert.Single(_h.Items);
        Assert.False(result.Single().Created);
        Assert.False(result.Single().Moved);
    }

    [Theory]
    [InlineData(ChildRoadmapItemStatus.Completed)]
    [InlineData(ChildRoadmapItemStatus.Skipped)]
    [InlineData(ChildRoadmapItemStatus.Removed)]
    public async Task FinishedStep_DoesNotBlockAddingAgain(ChildRoadmapItemStatus status)
    {
        _h.AddItem(1, ChildRoadmapHarness.Child, 10, status);

        var result = await InsertAsync(RoadmapInsertPosition.End, false, Step(10));

        Assert.True(result.Single().Created);
        Assert.Equal(2, _h.Items.Count);
    }

    [Fact]
    public async Task DuplicateStoryInOneRequest_IsInsertedOnce()
    {
        var result = await InsertAsync(RoadmapInsertPosition.End, false, Step(10), Step(10));

        Assert.Single(_h.Items);
        Assert.Equal(result[0].ItemId, result[1].ItemId);
    }

    [Fact]
    public async Task SavesOnceWithoutOpeningATransaction()
    {
        await InsertAsync(RoadmapInsertPosition.End, false, Step(10), Step(11));

        _h.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _h.UnitOfWork.Verify(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
