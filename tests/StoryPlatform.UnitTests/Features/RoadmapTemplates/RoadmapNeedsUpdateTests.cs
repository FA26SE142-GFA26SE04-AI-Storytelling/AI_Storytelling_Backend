using Moq;
using StoryPlatform.Application.Features.Moderation.Services;
using StoryPlatform.Application.Features.RoadmapTemplates.DTOs;
using StoryPlatform.Application.Features.RoadmapTemplates.Services;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.RoadmapTemplates;

public class RoadmapNeedsUpdateTests
{
    private readonly RoadmapHarness _h = new();
    private readonly RoadmapTemplateService _sut;

    private const int Moderator = RoadmapHarness.ModeratorId;
    private const int OtherModerator = RoadmapHarness.OtherModeratorId;

    public RoadmapNeedsUpdateTests()
    {
        var queue = new ModerationQueueService(_h.UnitOfWork.Object, _h.Audit.Object);
        _sut = new RoadmapTemplateService(
            _h.UnitOfWork.Object,
            new RoadmapSubmissionChecker(_h.UnitOfWork.Object),
            queue,
            _h.Audit.Object);
    }

    private void Published(int id = 2, params int[] storyIds) =>
        _h.AddRoadmap(
            id,
            id == 2 ? AgeBand.Age_6_8 : AgeBand.Age_9_10,
            id == 2 ? 5 : 6,
            RoadmapTemplateStatus.Published,
            OtherModerator,
            storyIds.Length == 0 ? [10, 11] : storyIds);

    private Task<StoryPlatform.Application.Common.Models.PagedResult<RoadmapListItemDto>> ListAsync() =>
        _sut.ListAsync(Moderator, new RoadmapListQueryDto { Group = RoadmapListGroup.Published });

    [Fact]
    public async Task PublishedRoadmapWithArchivedStories_IsFlaggedWithCountAndIds()
    {
        Published();
        _h.Stories.Single(story => story.Id == 10).Status = StoryStatus.Archived;
        _h.Stories.Single(story => story.Id == 11).ArchivedAt = DateTime.UtcNow;

        var row = Assert.Single((await ListAsync()).Items);
        var detail = await _sut.GetAsync(Moderator, 2);

        Assert.True(row.NeedsUpdate);
        Assert.Equal(2, row.RetiredStoryCount);
        Assert.True(detail.NeedsUpdate);
        Assert.Equal(new[] { 10, 11 }, detail.RetiredStoryIds);
        Assert.Equal("Published", detail.Status);
    }

    [Fact]
    public async Task MissingOrDeletedStory_CountsAsRetired()
    {
        Published(2, 10, 11, 999);
        _h.Stories.Single(story => story.Id == 11).IsDeleted = true;

        var row = Assert.Single((await ListAsync()).Items);

        Assert.Equal(2, row.RetiredStoryCount);
    }

    [Fact]
    public async Task NotReadyStory_IsNotRetired()
    {
        Published();
        _h.Stories.Single(story => story.Id == 11).Status = StoryStatus.MediaProcessing;

        var row = Assert.Single((await ListAsync()).Items);

        Assert.False(row.NeedsUpdate);
        Assert.Equal(0, row.RetiredStoryCount);
    }

    [Fact]
    public async Task OnlyAffectedPublishedRoadmapIsFlagged()
    {
        Published(2, 10, 11);
        Published(3, 12);
        _h.Stories.Single(story => story.Id == 11).Status = StoryStatus.Archived;

        var rows = (await ListAsync()).Items;

        Assert.True(rows.Single(row => row.Id == 2).NeedsUpdate);
        Assert.False(rows.Single(row => row.Id == 3).NeedsUpdate);
    }

    [Fact]
    public async Task NonPublishedGroupsNeverCarryLabel()
    {
        _h.AddRoadmap(2, AgeBand.Age_6_8, 5, RoadmapTemplateStatus.Draft, Moderator, 10, 11);
        _h.AddRoadmap(3, AgeBand.Age_9_10, 6, RoadmapTemplateStatus.Archived, OtherModerator, 10, 11);
        _h.Stories.Single(story => story.Id == 10).Status = StoryStatus.Archived;

        var draft = await _sut.ListAsync(
            Moderator, new RoadmapListQueryDto { Group = RoadmapListGroup.Drafts });
        var archived = await _sut.ListAsync(
            Moderator, new RoadmapListQueryDto { Group = RoadmapListGroup.Archived });

        Assert.False(Assert.Single(draft.Items).NeedsUpdate);
        Assert.False(Assert.Single(archived.Items).NeedsUpdate);
        Assert.False((await _sut.GetAsync(Moderator, 2)).NeedsUpdate);
    }

    [Fact]
    public async Task LabelDisappearsWhenHealthyReplacementIsPublished()
    {
        Published(2, 10, 11);
        _h.Stories.Single(story => story.Id == 11).Status = StoryStatus.Archived;
        Assert.True(Assert.Single((await ListAsync()).Items).NeedsUpdate);

        var old = _h.Roadmaps.Single(roadmap => roadmap.Id == 2);
        old.Status = RoadmapTemplateStatus.Archived;
        old.ArchivedAt = DateTime.UtcNow;
        Published(3, 10);

        var row = Assert.Single((await ListAsync()).Items);
        Assert.Equal(3, row.Id);
        Assert.False(row.NeedsUpdate);
    }

    [Fact]
    public async Task ReadingLabelDoesNotSendNotifications()
    {
        Published();
        _h.Stories.Single(story => story.Id == 11).Status = StoryStatus.Archived;

        await ListAsync();
        await _sut.GetAsync(Moderator, 2);

        _h.Notifications.Verify(notification => notification.CreateAsync(
            It.IsAny<int>(), It.IsAny<NotificationType>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }
}
