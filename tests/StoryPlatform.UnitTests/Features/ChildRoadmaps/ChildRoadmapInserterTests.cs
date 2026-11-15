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

    private Task<List<InsertedStep>> InsertToStageAsync(int stageNumber, params NewRoadmapStep[] steps) =>
        _sut.InsertAsync(new InsertRequest(
            ChildRoadmapHarness.Child, ChildRoadmapHarness.ParentId, steps, RoadmapInsertPosition.Stage,
            ChildRoadmapItemSource.Manual, null, true, stageNumber));

    private StoryPlatform.Domain.Entities.ChildRoadmapItem Story(int storyId) =>
        _h.Items.Single(item => item.StoryId == storyId);

    [Fact]
    public async Task Stage_JoinsAWaitingStage()
    {
        _h.AddItem(1, ChildRoadmapHarness.Child, 10, ChildRoadmapItemStatus.Open, position: 1);
        _h.AddItem(2, ChildRoadmapHarness.Child, 11, ChildRoadmapItemStatus.Waiting, position: 2);
        _h.AddItem(3, ChildRoadmapHarness.Child, 12, ChildRoadmapItemStatus.Waiting, position: 3);

        var result = await InsertToStageAsync(2, Step(13));

        Assert.True(result.Single().Created);
        Assert.Equal(ChildRoadmapItemStatus.Waiting, Story(13).Status);
        Assert.Equal(2, Story(13).Position);
        Assert.Equal(2, Story(11).Position);
        Assert.Equal(3, Story(12).Position);
    }

    [Fact]
    public async Task Stage_CurrentStage_OpensTheNewStoryNow()
    {
        _h.AddItem(1, ChildRoadmapHarness.Child, 10, ChildRoadmapItemStatus.Open, position: 1);
        var before = DateTime.UtcNow;

        await InsertToStageAsync(1, Step(11, days: 3));

        Assert.Equal(ChildRoadmapItemStatus.Open, Story(11).Status);
        Assert.Equal(1, Story(11).Position);
        Assert.True(Story(11).OpenedAt >= before);
        Assert.InRange(Story(11).ExpectedCompletionAt!.Value, before.AddDays(3), DateTime.UtcNow.AddDays(3));
    }

    [Fact]
    public async Task Stage_CurrentStage_MovesAWaitingStoryInAndOpensIt()
    {
        _h.AddItem(1, ChildRoadmapHarness.Child, 10, ChildRoadmapItemStatus.Open, position: 1);
        _h.AddItem(2, ChildRoadmapHarness.Child, 11, ChildRoadmapItemStatus.Waiting, position: 2);
        _h.AddItem(3, ChildRoadmapHarness.Child, 12, ChildRoadmapItemStatus.Waiting, position: 3);

        var result = await InsertToStageAsync(1, Step(12));

        Assert.True(result.Single().Moved);
        Assert.Equal(3, _h.Items.Count);
        Assert.Equal(ChildRoadmapItemStatus.Open, Story(12).Status);
        Assert.Equal(1, Story(12).Position);
        Assert.Equal(2, Story(11).Position);
    }

    [Fact]
    public async Task Stage_MovingTheOnlyStoryOfAStage_ClosesTheGap()
    {
        _h.AddItem(1, ChildRoadmapHarness.Child, 10, ChildRoadmapItemStatus.Open, position: 1);
        _h.AddItem(2, ChildRoadmapHarness.Child, 11, ChildRoadmapItemStatus.Waiting, position: 2);
        _h.AddItem(3, ChildRoadmapHarness.Child, 12, ChildRoadmapItemStatus.Waiting, position: 3);
        _h.AddItem(4, ChildRoadmapHarness.Child, 13, ChildRoadmapItemStatus.Waiting, position: 4);

        await InsertToStageAsync(4, Step(11));

        Assert.Equal(2, Story(12).Position);
        Assert.Equal(3, Story(13).Position);
        Assert.Equal(3, Story(11).Position);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    public async Task Stage_FinishedOrMissingStage_IsBadRequest(int stageNumber)
    {
        _h.AddItem(1, ChildRoadmapHarness.Child, 10, ChildRoadmapItemStatus.Completed, position: 1);
        _h.AddItem(2, ChildRoadmapHarness.Child, 11, ChildRoadmapItemStatus.Open, position: 2);
        _h.AddItem(3, ChildRoadmapHarness.Child, 12, ChildRoadmapItemStatus.Waiting, position: 3);

        var error = await Assert.ThrowsAsync<StoryPlatform.Application.Common.Exceptions.BadRequestException>(
            () => InsertToStageAsync(stageNumber, Step(13)));

        Assert.Equal($"Chặng {stageNumber} không còn nhận thêm truyện.", error.Message);
        Assert.Equal(3, _h.Items.Count);
    }

    [Fact]
    public async Task NextUp_NumbersNewStageRightAfterTheOpenStage()
    {
        _h.AddItem(1, ChildRoadmapHarness.Child, 10, ChildRoadmapItemStatus.Completed, position: 1);
        _h.AddItem(2, ChildRoadmapHarness.Child, 11, ChildRoadmapItemStatus.Open, position: 2);
        _h.AddItem(3, ChildRoadmapHarness.Child, 12, ChildRoadmapItemStatus.Waiting, position: 3);

        await InsertAsync(RoadmapInsertPosition.NextUp, false, Step(13));

        Assert.Equal(3, Story(13).Position);
        Assert.Equal(4, Story(12).Position);
    }

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

    // ---- RoadmapStages ----

    [Fact]
    public void Current_PrefersTheOpenStageThenTheLastFinishedStage()
    {
        _h.AddItem(1, ChildRoadmapHarness.Child, 10, ChildRoadmapItemStatus.Completed, position: 1);
        Assert.Equal(1, RoadmapStages.Current(_h.Items));

        _h.AddItem(2, ChildRoadmapHarness.Child, 11, ChildRoadmapItemStatus.Open, position: 2);
        _h.AddItem(3, ChildRoadmapHarness.Child, 12, ChildRoadmapItemStatus.Skipped, position: 9);
        Assert.Equal(2, RoadmapStages.Current(_h.Items));
        Assert.Equal(0, RoadmapStages.Current(Array.Empty<StoryPlatform.Domain.Entities.ChildRoadmapItem>()));
    }

    [Fact]
    public async Task RenumberWaiting_StartsAfterTheCurrentStageAndKeepsSharedStages()
    {
        _h.AddItem(1, ChildRoadmapHarness.Child, 10, ChildRoadmapItemStatus.Open, position: 4);
        _h.AddItem(2, ChildRoadmapHarness.Child, 11, ChildRoadmapItemStatus.Waiting, position: 7);
        _h.AddItem(3, ChildRoadmapHarness.Child, 12, ChildRoadmapItemStatus.Waiting, position: 7);
        _h.AddItem(4, ChildRoadmapHarness.Child, 13, ChildRoadmapItemStatus.Waiting, position: 9);

        await RoadmapStages.RenumberWaitingAsync(_h.UnitOfWork.Object, ChildRoadmapHarness.Child, CancellationToken.None);

        Assert.Equal(new[] { 4, 5, 5, 6 }, _h.Items.OrderBy(item => item.Id).Select(item => item.Position));
    }
}
