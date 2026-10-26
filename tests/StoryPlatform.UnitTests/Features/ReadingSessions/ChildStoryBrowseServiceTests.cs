using Moq;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ChildRoadmaps;
using StoryPlatform.Application.Features.ChildRoadmaps.Interfaces;
using StoryPlatform.Application.Features.ReadingSessions;
using StoryPlatform.Application.Features.ReadingSessions.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ReadingSessions;

public class ChildStoryBrowseServiceTests
{
    private readonly ReadingHarness _h = new();
    private readonly Mock<IChildRoadmapEligibility> _eligibility = new();
    private readonly ChildStoryBrowseService _sut;

    public ChildStoryBrowseServiceTests()
    {
        _eligibility.Setup(e => e.EvaluateAsync(It.IsAny<ChildProfile>(), It.IsAny<Story?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChildRoadmapSkipReason?)null);
        _sut = new ChildStoryBrowseService(_h.UnitOfWork.Object, _eligibility.Object);
    }

    private Story AddCreatorStory(int id, string title)
    {
        var story = new Story
        {
            Id = id, Title = title, Scope = StoryScope.Creator, AgeBand = "6-8", IsPublished = true,
            Status = StoryStatus.Ready, AuthorUserId = 8
        };
        _h.Stories.Add(story);
        return story;
    }

    [Fact]
    public async Task SearchAsync_ShowsOwnAndLibraryStories()
    {
        AddCreatorStory(20, "Thư viện 20");

        var page = await _sut.SearchAsync(ReadingHarness.Child, new ChildStoryQueryDto());

        Assert.False(page.IsLocked);
        Assert.Equal(new[] { 10, 20 }, page.Items.Select(i => i.StoryId).OrderBy(id => id));
        Assert.Equal("mine", page.Items.Single(i => i.StoryId == 10).Source);
        Assert.Equal("library", page.Items.Single(i => i.StoryId == 20).Source);
    }

    [Fact]
    public async Task SearchAsync_HidesAnotherChildsPrivateStory()
    {
        _h.Stories.Add(new Story
        {
            Id = 30, Title = "Của bé khác", Scope = StoryScope.Parent, AgeBand = "6-8", Status = StoryStatus.Ready,
            AuthorUserId = 7, ChildProfileId = ReadingHarness.OtherChild
        });

        var page = await _sut.SearchAsync(ReadingHarness.Child, new ChildStoryQueryDto());

        Assert.DoesNotContain(page.Items, i => i.StoryId == 30);
    }

    [Fact]
    public async Task SearchAsync_IncludesParentSelectedSiblingStoryOfAnotherAge_OnlyWhileAssigned()
    {
        var story = _h.Stories.Single(); story.ChildProfileId = ReadingHarness.OtherChild; story.AgeBand = "9-10";
        var selection = new ChildRoadmapItem { Id = 7, ChildProfileId = ReadingHarness.Child,
            StoryId = story.Id, Source = ChildRoadmapItemSource.Manual, Status = ChildRoadmapItemStatus.Waiting };
        _h.RoadmapItems.Add(selection);
        var service = new ChildStoryBrowseService(_h.UnitOfWork.Object,
            new StoryPlatform.Application.Features.ChildRoadmaps.Services.ChildRoadmapEligibility(_h.UnitOfWork.Object));
        var page = await service.SearchAsync(ReadingHarness.Child, new ChildStoryQueryDto { Source = "mine" });
        Assert.Contains(page.Items, item => item.StoryId == story.Id);
        selection.Status = ChildRoadmapItemStatus.Removed;
        Assert.Empty((await service.SearchAsync(ReadingHarness.Child, new ChildStoryQueryDto())).Items);
    }

    [Fact]
    public async Task SearchAsync_FiltersByTitleAndSource()
    {
        AddCreatorStory(20, "Chú thỏ con");

        var byTitle = await _sut.SearchAsync(ReadingHarness.Child, new ChildStoryQueryDto { Search = "thỏ" });
        var mineOnly = await _sut.SearchAsync(ReadingHarness.Child, new ChildStoryQueryDto { Source = "mine" });

        Assert.Equal(new[] { 20 }, byTitle.Items.Select(i => i.StoryId));
        Assert.Equal(new[] { 10 }, mineOnly.Items.Select(i => i.StoryId));
    }

    [Fact]
    public async Task SearchAsync_DropsStoriesTheSafetyPolicyRejects()
    {
        _eligibility.Setup(e => e.EvaluateAsync(It.IsAny<ChildProfile>(), It.Is<Story?>(s => s!.Id == 10), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ChildRoadmapSkipReason.BlockedTopic);

        var page = await _sut.SearchAsync(ReadingHarness.Child, new ChildStoryQueryDto());

        Assert.Empty(page.Items);
    }

    [Fact]
    public async Task SearchAsync_HoldMode_ReturnsLockedWithUnlockStoryOnly()
    {
        _h.AddHoldMode(ReadingHarness.Child, triggeringStoryId: 10);
        AddCreatorStory(20, "Thư viện 20");

        var page = await _sut.SearchAsync(ReadingHarness.Child, new ChildStoryQueryDto());

        Assert.True(page.IsLocked);
        Assert.Equal(10, page.UnlockStoryId);
        Assert.Equal(ChildReadingPolicy.LibraryLockedMessage, page.Message);
        Assert.Empty(page.Items);
    }

    [Fact]
    public async Task SearchAsync_InactiveProfile_IsForbidden()
    {
        _h.Children.Single(c => c.Id == ReadingHarness.Child).Status = ChildProfileStatus.Suspended;

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.SearchAsync(ReadingHarness.Child, new ChildStoryQueryDto()));
    }

    [Theory]
    [InlineData("draft")]
    [InlineData("archived")]
    [InlineData("deleted")]
    [InlineData("unpublished")]
    [InlineData("wrong-age")]
    public async Task SearchAsync_HidesUnavailableLibraryStories(string state)
    {
        var story = AddCreatorStory(20, "Thư viện");
        if (state == "draft") story.Status = StoryStatus.Draft;
        if (state == "archived") story.ArchivedAt = DateTime.UtcNow;
        if (state == "deleted") story.IsDeleted = true;
        if (state == "unpublished") story.IsPublished = false;
        if (state == "wrong-age") story.AgeBand = "9-10";

        var page = await _sut.SearchAsync(ReadingHarness.Child, new ChildStoryQueryDto { Source = "library" });

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
    }

    [Fact]
    public async Task SearchAsync_NormalizesQueryAndPaginatesDeterministically()
    {
        AddCreatorStory(20, "Chú thỏ con").CreatedAt = new DateTime(2026, 1, 1);
        AddCreatorStory(21, "Chú thỏ trắng").CreatedAt = new DateTime(2026, 1, 1);

        var page = await _sut.SearchAsync(ReadingHarness.Child,
            new ChildStoryQueryDto { Search = " THỎ ", Source = " LIBRARY ", PageIndex = 2, PageSize = 1 });

        Assert.Equal(20, Assert.Single(page.Items).StoryId);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(2, page.PageIndex);
        Assert.Equal(1, page.PageSize);
    }

    [Theory]
    [InlineData(0, 0, 1, 1)]
    [InlineData(-5, 100, 1, 50)]
    public async Task SearchAsync_ClampsPageBounds(int index, int size, int expectedIndex, int expectedSize)
    {
        var page = await _sut.SearchAsync(ReadingHarness.Child,
            new ChildStoryQueryDto { PageIndex = index, PageSize = size });

        Assert.Equal(expectedIndex, page.PageIndex);
        Assert.Equal(expectedSize, page.PageSize);
    }
}
