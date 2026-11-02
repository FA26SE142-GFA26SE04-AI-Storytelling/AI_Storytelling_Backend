using Moq;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.Services;
using StoryPlatform.Application.Features.ChildRoadmaps;
using StoryPlatform.Application.Features.ChildRoadmaps.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ChildRoadmaps;

public class RoadmapLibraryServiceTests
{
    private readonly ChildRoadmapHarness _h = new();
    private readonly RoadmapLibraryService _sut;

    private const int Child = ChildRoadmapHarness.Child;
    private const int OtherChild = ChildRoadmapHarness.OtherChild;
    private const int Owner = ChildRoadmapHarness.ParentId;
    private const int Second = ChildRoadmapHarness.SecondSupervisorId;

    public RoadmapLibraryServiceTests()
    {
        var eligibility = new ChildRoadmapEligibility(_h.UnitOfWork.Object);
        _sut = new RoadmapLibraryService(
            _h.UnitOfWork.Object,
            new ChildProfileAccessGuard(_h.UnitOfWork.Object),
            eligibility,
            new ChildRoadmapInserter(_h.UnitOfWork.Object),
            new ChildRoadmapAdvancer(_h.UnitOfWork.Object, eligibility),
            new ChildRoadmapNotifier(_h.UnitOfWork.Object, _h.Notifications.Object),
            _h.Audit.Object);
    }

    private ReadingRoadmap Published(int id = 1, int categoryId = 5, params int[] storyIds) =>
        _h.AddTemplate(
            id,
            AgeBand.Age_6_8,
            categoryId,
            RoadmapTemplateStatus.Published,
            storyIds.Length == 0 ? new[] { 10, 11, 12 } : storyIds);

    private static RoadmapCopyRequestDto Copy(
        int[] children,
        RoadmapInsertPosition position = RoadmapInsertPosition.End,
        int? days = null) => new()
    {
        ChildProfileIds = children.ToList(),
        Position = position,
        DaysPerStory = days
    };

    private int[] WaitingStories() => _h.Items
        .Where(item => item.ChildProfileId == Child && item.Status == ChildRoadmapItemStatus.Waiting)
        .OrderBy(item => item.Position).Select(item => item.StoryId).ToArray();

    [Fact]
    public async Task List_ReturnsOnlyPublishedUnarchivedRoadmapsNewestFirst()
    {
        Published(1).PublishedAt = DateTime.UtcNow.AddDays(-5);
        Published(2, 6).PublishedAt = DateTime.UtcNow.AddDays(-1);
        _h.AddTemplate(3, AgeBand.Age_6_8, 5, RoadmapTemplateStatus.Draft, 10);
        _h.AddTemplate(4, AgeBand.Age_6_8, 5, RoadmapTemplateStatus.Archived, 10);
        Published(5).ArchivedAt = DateTime.UtcNow;

        var result = await _sut.ListAsync(Owner, new RoadmapLibraryQueryDto());

        Assert.Equal(new[] { 2, 1 }, result.Items.Select(item => item.Id));
        Assert.Equal(2, result.TotalCount);
    }

    [Fact]
    public async Task List_FiltersAndReturnsNeedsUpdateMetadata()
    {
        var roadmap = Published(1);
        roadmap.ReadingLevelMax = 4;
        _h.Stories.Single(story => story.Id == 12).Status = StoryStatus.Rejected;
        _h.AddTemplate(2, AgeBand.Age_9_10, 6, RoadmapTemplateStatus.Published, 10)
            .ReadingLevelMin = 5;

        var result = await _sut.ListAsync(Owner, new RoadmapLibraryQueryDto
            { AgeBand = "6-8", ReadingLevel = 3, ContentCategoryId = 5 });

        var row = Assert.Single(result.Items);
        Assert.Equal("friendship", row.MainCategory!.Code);
        Assert.Equal(3, row.StoryCount);
        Assert.True(row.NeedsUpdate);
    }

    [Fact]
    public async Task List_ValidatesAgeAndRequiresActiveParent()
    {
        Published();
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.ListAsync(
            Owner, new RoadmapLibraryQueryDto { AgeBand = "11-12" }));
        _h.AddUser(30, UserRole.Moderator);
        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.ListAsync(
            30, new RoadmapLibraryQueryDto()));
    }

    [Fact]
    public async Task Get_ReturnsOrderedStoriesWithDaysAndAvailability()
    {
        Published(1, 5, 12, 10, 11);
        _h.SetItemDays(1, 10, 5);
        _h.Stories.Single(story => story.Id == 11).Status = StoryStatus.Rejected;

        var detail = await _sut.GetAsync(Owner, 1);

        Assert.Equal(new[] { 12, 10, 11 }, detail.Stories.Select(story => story.StoryId));
        Assert.Equal(5, detail.Stories[1].Days);
        Assert.False(detail.Stories[2].IsAvailable);
        Assert.True(detail.NeedsUpdate);
    }

    [Theory]
    [InlineData(RoadmapTemplateStatus.Draft)]
    [InlineData(RoadmapTemplateStatus.PendingReview)]
    [InlineData(RoadmapTemplateStatus.Archived)]
    public async Task Get_NonPublishedTemplate_IsNotFound(RoadmapTemplateStatus status)
    {
        _h.AddTemplate(1, AgeBand.Age_6_8, 5, status, 10);
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetAsync(Owner, 1));
    }

    [Fact]
    public async Task Preview_ListsCopyAndSkippedStepsWithoutWriting()
    {
        Published(1, 5, 12, 10, 11);
        _h.SetItemDays(1, 10, 5);
        _h.Block(Child, 5);
        _h.Tag(11, 5);

        var plan = await _sut.PreviewAsync(Owner, 1, Copy(new[] { Child }));

        var child = Assert.Single(plan.Children);
        Assert.Equal("Copy", child.Outcome);
        Assert.Equal(new[] { 2, 5, 2 }, child.Steps.Select(step => step.Days));
        Assert.Equal(ChildRoadmapSkipCodes.BlockedTopic,
            child.Steps.Single(step => step.StoryId == 11).SkipCode);
        Assert.Empty(_h.Items);
        _h.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("wrong-age", "wrong_age")]
    [InlineData("too-long", "too_long")]
    [InlineData("archived", "story_unavailable")]
    [InlineData("deleted", "story_unavailable")]
    public async Task Preview_MapsEligibilityProblemsToSkipCodes(string problem, string code)
    {
        Published();
        var story = _h.Stories.Single(existing => existing.Id == 10);
        if (problem == "wrong-age") story.AgeBand = "9-10";
        if (problem == "too-long")
        {
            _h.AddPolicy(Child, 100);
            story.Content = string.Join(' ', Enumerable.Repeat("bé", 500));
        }
        if (problem == "archived") story.Status = StoryStatus.Rejected;
        if (problem == "deleted") story.IsDeleted = true;

        var child = (await _sut.PreviewAsync(Owner, 1, Copy(new[] { Child }))).Children.Single();
        Assert.Equal(code, child.Steps.Single(step => step.StoryId == 10).SkipCode);
    }

    [Fact]
    public async Task Preview_DetectsDuplicatesAndReadingLevelWarning()
    {
        Published();
        _h.AddItem(1, Child, 10, ChildRoadmapItemStatus.Open);
        _h.AddLearning(Child, 7);

        var child = (await _sut.PreviewAsync(Owner, 1, Copy(new[] { Child }))).Children.Single();

        Assert.Equal(ChildRoadmapSkipCodes.AlreadyInRoadmap,
            child.Steps.Single(step => step.StoryId == 10).SkipCode);
        Assert.Contains("reading_level_outside_range", child.Warnings);
    }

    [Fact]
    public async Task Preview_EvaluatesPermissionAndProfilePerChild()
    {
        Published();
        _h.Children.Single(child => child.Id == OtherChild).Status = ChildProfileStatus.Suspended;

        var plan = await _sut.PreviewAsync(Owner, 1, Copy(new[] { Child, OtherChild, 999 }));

        Assert.Equal("Copy", plan.Children.Single(child => child.ChildProfileId == Child).Outcome);
        Assert.Equal(ChildRoadmapSkipCodes.ProfileInactive,
            plan.Children.Single(child => child.ChildProfileId == OtherChild).SkipCode);
        Assert.Equal(ChildRoadmapSkipCodes.NoPermission,
            plan.Children.Single(child => child.ChildProfileId == 999).SkipCode);
    }

    [Fact]
    public async Task Preview_ValidatesRequest()
    {
        Published();
        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.PreviewAsync(Owner, 1, Copy(Array.Empty<int>())));
        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.PreviewAsync(Owner, 1, Copy(new[] { Child }, days: 91)));
    }

    [Fact]
    public async Task Copy_EmptyRoadmapCopiesInOrderOpensFirstAndStoresSource()
    {
        Published(1, 5, 12, 10, 11);
        _h.SetItemDays(1, 10, 5);

        var result = await _sut.CopyAsync(Owner, 1, Copy(new[] { Child }));

        Assert.Equal("Copied", result.Children.Single().Outcome);
        var items = _h.Items.OrderBy(item => item.Position).ToList();
        Assert.Equal(new[] { 12, 10, 11 }, items.Select(item => item.StoryId));
        Assert.Equal(ChildRoadmapItemStatus.Open, items[0].Status);
        Assert.All(items, item => Assert.Equal(ChildRoadmapItemSource.TemplateCopy, item.Source));
        Assert.All(items, item => Assert.Equal(1, item.SourceReadingRoadmapId));
        Assert.Equal(new int?[] { 2, 5, 2 }, items.Select(item => item.ExpectedDays));
    }

    [Fact]
    public async Task Copy_NextUpPreservesTemplateOrderAheadOfExistingQueue()
    {
        _h.AddStory(14);
        _h.AddItem(1, Child, 13, ChildRoadmapItemStatus.Open);
        _h.AddItem(2, Child, 14, ChildRoadmapItemStatus.Waiting);
        Published();

        await _sut.CopyAsync(Owner, 1, Copy(new[] { Child }, RoadmapInsertPosition.NextUp));

        Assert.Equal(new[] { 10, 11, 12, 14 }, WaitingStories());
    }

    [Fact]
    public async Task Copy_SkipsInvalidAndExistingStories()
    {
        Published();
        _h.AddItem(1, Child, 10, ChildRoadmapItemStatus.Waiting);
        _h.Stories.Single(story => story.Id == 11).Status = StoryStatus.Rejected;

        var result = await _sut.CopyAsync(Owner, 1, Copy(new[] { Child }));

        Assert.Equal(2, _h.Items.Count);
        Assert.Equal(ChildRoadmapSkipCodes.AlreadyInRoadmap,
            result.Children.Single().Steps.Single(step => step.StoryId == 10).SkipCode);
        Assert.Equal(ChildRoadmapSkipCodes.StoryUnavailable,
            result.Children.Single().Steps.Single(step => step.StoryId == 11).SkipCode);
    }

    [Fact]
    public async Task Copy_HoldModeKeepsCopiedStoriesWaiting()
    {
        Published();
        _h.AddHoldMode(Child);

        await _sut.CopyAsync(Owner, 1, Copy(new[] { Child }));

        Assert.All(_h.Items, item => Assert.Equal(ChildRoadmapItemStatus.Waiting, item.Status));
    }

    [Fact]
    public async Task Copy_NotifiesOnceAuditsAndCommitsPerChild()
    {
        Published();

        await _sut.CopyAsync(Owner, 1, Copy(new[] { Child }));

        _h.Notifications.Verify(n => n.CreateAsync(
            Owner,
            NotificationType.RoadmapStoryAdded,
            It.Is<string?>(payload => payload!.Contains("\"roadmapId\":1") && payload.Contains("\"count\":3")),
            It.IsAny<CancellationToken>()), Times.Never);
        _h.Audit.Verify(a => a.LogAsync(
            Owner, "ChildRoadmapTemplateCopied", nameof(ChildProfile), Child,
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
        _h.UnitOfWork.Verify(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Copy_NoValidStorySkipsWithoutNotification()
    {
        Published();
        foreach (var story in _h.Stories.Where(story => new[] { 10, 11, 12 }.Contains(story.Id)))
        {
            story.Status = StoryStatus.Rejected;
        }

        var result = await _sut.CopyAsync(Owner, 1, Copy(new[] { Child }));

        Assert.Equal(RoadmapLibrarySkipCodes.NoValidStory, result.Children.Single().SkipCode);
        Assert.Empty(_h.Items);
        _h.Notifications.Verify(n => n.CreateAsync(
            It.IsAny<int>(), It.IsAny<NotificationType>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData(RoadmapTemplateStatus.Draft)]
    [InlineData(RoadmapTemplateStatus.PendingReview)]
    [InlineData(RoadmapTemplateStatus.Archived)]
    public async Task Copy_NonPublishedTemplateIsConflict(RoadmapTemplateStatus status)
    {
        _h.AddTemplate(1, AgeBand.Age_6_8, 5, status, 10);
        await Assert.ThrowsAsync<ConflictException>(() =>
            _sut.CopyAsync(Owner, 1, Copy(new[] { Child })));
    }
}
