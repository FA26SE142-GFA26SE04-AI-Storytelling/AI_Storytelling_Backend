using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ChildRoadmaps;
using StoryPlatform.Application.Features.ChildRoadmaps.Services;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ChildRoadmaps;

public class MyRoadmapServiceTests
{
    private readonly ChildRoadmapHarness _h = new();
    private readonly MyRoadmapService _sut;

    private const int Child = ChildRoadmapHarness.Child;
    private const int OtherChild = ChildRoadmapHarness.OtherChild;

    public MyRoadmapServiceTests()
    {
        _sut = new MyRoadmapService(_h.UnitOfWork.Object);
    }

    [Fact]
    public async Task GetAsync_ShowsTheOpenStoryWithNoteAndFutureDate()
    {
        var open = _h.AddItem(1, Child, 10, ChildRoadmapItemStatus.Open);
        open.Note = "Đọc kỹ nhé";
        open.ExpectedCompletionAt = DateTime.UtcNow.AddDays(2);
        _h.Stories.Single(story => story.Id == 10).CoverImageUrl = "https://cdn.test/10.png";

        var view = await _sut.GetAsync(Child);

        Assert.Equal(10, view.Open!.StoryId);
        Assert.Equal("Truyện 10", view.Open.Title);
        Assert.Equal("https://cdn.test/10.png", view.Open.CoverImageUrl);
        Assert.Equal("Đọc kỹ nhé", view.Open.Note);
        Assert.Equal(open.ExpectedCompletionAt, view.Open.ExpectedBy);
        Assert.False(view.Open.IsLocked);
    }

    [Fact]
    public async Task GetAsync_PastExpectedDate_IsNeverShownAsOverdue()
    {
        var open = _h.AddItem(1, Child, 10, ChildRoadmapItemStatus.Open);
        open.ExpectedCompletionAt = DateTime.UtcNow.AddDays(-3);

        Assert.Null((await _sut.GetAsync(Child)).Open!.ExpectedBy);
    }

    [Fact]
    public async Task GetAsync_NoExpectedDate_HasNoExpectedBy()
    {
        _h.AddItem(1, Child, 10, ChildRoadmapItemStatus.Open);

        Assert.Null((await _sut.GetAsync(Child)).Open!.ExpectedBy);
    }

    [Fact]
    public async Task GetAsync_LaterStoriesAreLockedAndOrderedByPosition()
    {
        _h.AddItem(1, Child, 10, ChildRoadmapItemStatus.Open);
        _h.AddItem(2, Child, 12, ChildRoadmapItemStatus.Waiting, position: 2);
        _h.AddItem(3, Child, 11, ChildRoadmapItemStatus.Waiting, position: 1);

        var view = await _sut.GetAsync(Child);

        Assert.Equal(new[] { 11, 12 }, view.Upcoming.Select(story => story.StoryId));
        Assert.Equal(new[] { 1, 2 }, view.Upcoming.Select(story => story.Order));
        Assert.All(view.Upcoming, story => Assert.True(story.IsLocked));
        Assert.Equal("Truyện 11", view.Upcoming[0].Title);
    }

    [Fact]
    public async Task GetAsync_CompletedStoriesAreListedNewestFirstWithProgress()
    {
        var first = _h.AddItem(1, Child, 10, ChildRoadmapItemStatus.Completed);
        first.CompletedAt = DateTime.UtcNow.AddDays(-3);
        var second = _h.AddItem(2, Child, 11, ChildRoadmapItemStatus.Completed);
        second.CompletedAt = DateTime.UtcNow.AddDays(-1);
        _h.AddItem(3, Child, 12, ChildRoadmapItemStatus.Open);
        _h.AddItem(4, Child, 13, ChildRoadmapItemStatus.Waiting);

        var view = await _sut.GetAsync(Child);

        Assert.Equal(new[] { 11, 10 }, view.Done.Select(story => story.StoryId));
        Assert.Equal(2, view.CompletedCount);
        Assert.Equal(4, view.TotalCount);
        Assert.False(view.IsFinished);
    }

    [Fact]
    public async Task GetAsync_DoneListIsLimited()
    {
        for (var i = 1; i <= 55; i++)
        {
            var step = _h.AddItem(i, Child, 10, ChildRoadmapItemStatus.Completed);
            step.CompletedAt = DateTime.UtcNow.AddMinutes(-i);
        }

        var view = await _sut.GetAsync(Child);

        Assert.Equal(MyRoadmapPolicy.DoneLimit, view.Done.Count);
        Assert.Equal(55, view.CompletedCount);
    }

    [Fact]
    public async Task GetAsync_NothingLeftAfterReading_IsFinished()
    {
        _h.AddItem(1, Child, 10, ChildRoadmapItemStatus.Completed);

        var view = await _sut.GetAsync(Child);

        Assert.True(view.IsFinished);
        Assert.Null(view.Open);
        Assert.Empty(view.Upcoming);
        Assert.Equal(1, view.CompletedCount);
        Assert.Equal(1, view.TotalCount);
    }

    [Fact]
    public async Task GetAsync_EmptyRoadmap_IsNotFinished()
    {
        var view = await _sut.GetAsync(Child);

        Assert.False(view.IsFinished);
        Assert.Equal(0, view.TotalCount);
    }

    [Fact]
    public async Task GetAsync_WaitingStoriesRemain_IsNotFinished()
    {
        _h.AddItem(1, Child, 10, ChildRoadmapItemStatus.Completed);
        _h.AddItem(2, Child, 11, ChildRoadmapItemStatus.Waiting);

        Assert.False((await _sut.GetAsync(Child)).IsFinished);
    }

    [Theory]
    [InlineData(ChildRoadmapItemStatus.Skipped)]
    [InlineData(ChildRoadmapItemStatus.Removed)]
    public async Task GetAsync_SkippedAndRemovedStepsAreInvisible(ChildRoadmapItemStatus status)
    {
        var hidden = _h.AddItem(1, Child, 10, status);
        hidden.SkipReason = status == ChildRoadmapItemStatus.Skipped ? ChildRoadmapSkipReason.BlockedTopic : null;
        _h.AddItem(2, Child, 11, ChildRoadmapItemStatus.Open);

        var view = await _sut.GetAsync(Child);

        Assert.Equal(11, view.Open!.StoryId);
        Assert.Empty(view.Upcoming);
        Assert.Empty(view.Done);
        Assert.Equal(1, view.TotalCount);
    }

    [Fact]
    public async Task GetAsync_ArchivedOrDeletedStories_DisappearFromEveryList()
    {
        _h.AddItem(1, Child, 10, ChildRoadmapItemStatus.Open);
        _h.AddItem(2, Child, 11, ChildRoadmapItemStatus.Waiting);
        var done = _h.AddItem(3, Child, 12, ChildRoadmapItemStatus.Completed);
        done.CompletedAt = DateTime.UtcNow;
        _h.AddItem(4, Child, 13, ChildRoadmapItemStatus.Waiting);
        _h.Stories.Single(story => story.Id == 10).Status = StoryStatus.Archived;
        _h.Stories.Single(story => story.Id == 11).IsDeleted = true;
        _h.Stories.Single(story => story.Id == 12).ArchivedAt = DateTime.UtcNow;

        var view = await _sut.GetAsync(Child);

        Assert.Null(view.Open);
        Assert.Equal(new[] { 13 }, view.Upcoming.Select(story => story.StoryId));
        Assert.Empty(view.Done);
        Assert.Equal(1, view.TotalCount);
        Assert.Equal(0, view.CompletedCount);
    }

    [Fact]
    public void ResponseCarriesNoSensitiveRoadmapFields()
    {
        var names = typeof(MyRoadmapOpenStoryDto).GetProperties().Select(property => property.Name)
            .Concat(typeof(MyRoadmapUpcomingDto).GetProperties().Select(property => property.Name))
            .Concat(typeof(MyRoadmapDoneDto).GetProperties().Select(property => property.Name))
            .Concat(typeof(MyRoadmapDto).GetProperties().Select(property => property.Name)).ToList();

        Assert.DoesNotContain(names, name => name.Contains("Skip", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, name => name.Contains("Source", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, name => name.Contains("AddedBy", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, name => name.Contains("Hold", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, name => name.Contains("Overdue", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, name => name.Contains("Reason", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(InterventionStatus.OpenHoldMode)]
    [InlineData(InterventionStatus.UnderReview)]
    public async Task GetAsync_HoldMode_LocksTheOpenStoryNeutrally(InterventionStatus status)
    {
        _h.AddHoldMode(Child, status);
        _h.AddItem(1, Child, 10, ChildRoadmapItemStatus.Open);

        var view = await _sut.GetAsync(Child);

        Assert.True(view.Open!.IsLocked);
        Assert.Equal(10, view.Open.StoryId);
    }

    [Fact]
    public async Task GetAsync_HoldModeResolvedOrOfAnotherChild_DoesNotLock()
    {
        _h.AddHoldMode(Child, InterventionStatus.ResolvedUnlocked);
        _h.AddHoldMode(OtherChild);
        _h.AddItem(1, Child, 10, ChildRoadmapItemStatus.Open);

        Assert.False((await _sut.GetAsync(Child)).Open!.IsLocked);
    }

    [Fact]
    public async Task GetAsync_OnlyTheOwnRoadmapIsShown()
    {
        _h.AddItem(1, OtherChild, 10, ChildRoadmapItemStatus.Open);
        _h.AddItem(2, Child, 11, ChildRoadmapItemStatus.Open);

        var view = await _sut.GetAsync(Child);

        Assert.Equal(11, view.Open!.StoryId);
        Assert.Equal(1, view.TotalCount);
    }

    [Theory]
    [InlineData(ChildProfileStatus.Suspended)]
    [InlineData(ChildProfileStatus.Archived)]
    [InlineData(ChildProfileStatus.Deleted)]
    [InlineData(ChildProfileStatus.Draft)]
    public async Task GetAsync_ProfileNotActive_IsForbidden(ChildProfileStatus status)
    {
        _h.Children.Single(child => child.Id == Child).Status = status;

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.GetAsync(Child));
    }

    [Fact]
    public async Task GetAsync_UnknownOrDeletedProfile_IsForbidden()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.GetAsync(999));

        _h.Children.Single(child => child.Id == Child).IsDeleted = true;
        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.GetAsync(Child));
    }
}
