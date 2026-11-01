using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Moq;
using StoryPlatform.Api.Controllers;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ContentReports.DTOs;
using StoryPlatform.Application.Features.ContentReports.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ContentReports;

public class PublishedContentReviewServiceTests
{
    private readonly ContentReportHarness _h = new();
    private readonly PublishedContentReviewService _sut;

    private const int Moderator = ContentReportHarness.ModeratorId;
    private const int PublicStory = ContentReportHarness.PublicStoryId;

    public PublishedContentReviewServiceTests()
    {
        _sut = new PublishedContentReviewService(
            _h.UnitOfWork.Object, _h.Audit.Object);
    }

    [Fact]
    public async Task ListAsync_PublicLibrary_ReturnsOnlyPublishedCreatorStoriesNewestFirst()
    {
        var now = DateTime.UtcNow;
        _h.Stories.Single(story => story.Id == PublicStory).PublishedAt = now.AddDays(-5);
        _h.AddPublicStory(30, "9-10", now.AddDays(-1));
        var archived = _h.AddPublicStory(31, "6-8", now);
        archived.Status = StoryStatus.Rejected;

        var result = await _sut.ListAsync(Moderator, new PublishedContentQueryDto
        {
            Source = PublishedContentSource.PublicLibrary
        });

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(new[] { 30, PublicStory }, result.Items.Select(item => item.Id));
        Assert.All(result.Items, item => Assert.Equal("PublicLibrary", item.Source));
        Assert.All(result.Items, item => Assert.Null(item.StoryCount));
    }

    [Fact]
    public async Task ListAsync_PublicLibrary_AppliesAgeCategoryDateAndPaging()
    {
        var now = DateTime.UtcNow;
        _h.ContentCategories.Add(new ContentCategory
        {
            Id = 5, Code = "friendship", DisplayName = "Tình bạn"
        });
        for (var id = 30; id < 33; id++)
        {
            _h.AddPublicStory(id, "9-10", now.AddDays(id - 33));
            _h.StoryCategories.Add(new StoryCategory
            {
                Id = id, StoryId = id, ContentCategoryId = 5
            });
        }

        var result = await _sut.ListAsync(Moderator, new PublishedContentQueryDto
        {
            Source = PublishedContentSource.PublicLibrary,
            AgeBand = "9-10",
            ContentCategoryId = 5,
            PublishedFrom = now.AddDays(-3),
            PublishedTo = now,
            PageIndex = 2,
            PageSize = 1
        });

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(31, Assert.Single(result.Items).Id);
        Assert.Equal("friendship", Assert.Single(result.Items).Labels.Single().Code);
    }

    [Fact]
    public async Task ListAsync_RoadmapTemplate_ReturnsMetadataAndStoryCount()
    {
        _h.ContentCategories.Add(new ContentCategory
        {
            Id = 5, Code = "friendship", DisplayName = "Tình bạn"
        });
        _h.AddRoadmap(1, AgeBand.Age_6_8, 5, DateTime.UtcNow, PublicStory, 999);

        var result = await _sut.ListAsync(Moderator, new PublishedContentQueryDto
        {
            Source = PublishedContentSource.RoadmapTemplate,
            AgeBand = "6-8",
            ContentCategoryId = 5
        });

        var item = Assert.Single(result.Items);
        Assert.Equal("RoadmapTemplate", item.Source);
        Assert.Equal(2, item.StoryCount);
        Assert.Equal("friendship", item.Labels.Single().Code);
    }

    [Fact]
    public async Task ListAsync_RejectsInvalidQueryAndInactiveActor()
    {
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.ListAsync(Moderator,
            new PublishedContentQueryDto
            {
                Source = PublishedContentSource.PublicLibrary,
                AgeBand = "5-7"
            }));
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.ListAsync(Moderator,
            new PublishedContentQueryDto
            {
                Source = PublishedContentSource.PublicLibrary,
                PublishedFrom = DateTime.UtcNow,
                PublishedTo = DateTime.UtcNow.AddDays(-1)
            }));

        _h.Users.Single(user => user.Id == Moderator).Status = AccountStatus.Suspended;
        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.ListAsync(Moderator,
            new PublishedContentQueryDto { Source = PublishedContentSource.PublicLibrary }));
    }

    [Fact]
    public async Task GetStoryContentAsync_DoesNotRequireReviewView()
    {
        var result = await _sut.GetStoryContentAsync(Moderator, PublicStory);

        Assert.Equal(PublicStory, result.Story.StoryId);
    }

    [Fact]
    public async Task GetStoryContentAsync_ReturnsMaskedDisplayedVersionAndAuditsEveryOpen()
    {
        var story = _h.Stories.Single(candidate => candidate.Id == PublicStory);
        story.Title = "Gọi 0912345678";
        _h.Versions.Single(version => version.Id == 50).Content = "mail me@example.com";

        var first = await _sut.GetStoryContentAsync(Moderator, PublicStory);
        await _sut.GetStoryContentAsync(Moderator, PublicStory);

        Assert.Equal("Gọi [số điện thoại]", first.Story.Title);
        Assert.Equal("mail [email]", first.Version!.Content);
        _h.Audit.Verify(audit => audit.LogAsync(
            Moderator, "PublishedContentOpened", nameof(Story), PublicStory,
            null, It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task GetStoryContentAsync_RejectsPrivateOrArchivedStory()
    {
        var privateStory = ContentReportHarness.PrivateStoryId;
        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.GetStoryContentAsync(Moderator, privateStory));

        _h.Stories.Single(story => story.Id == PublicStory).Status = StoryStatus.Rejected;
        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.GetStoryContentAsync(Moderator, PublicStory));
    }

    [Fact]
    public async Task GetRoadmapAsync_ReturnsStoriesInPositionOrderWithoutReviewView()
    {
        _h.ContentCategories.Add(new ContentCategory
        {
            Id = 5, Code = "friendship", DisplayName = "Tình bạn"
        });
        var roadmap = _h.AddRoadmap(
            1, AgeBand.Age_6_8, 5, DateTime.UtcNow,
            ContentReportHarness.PrivateStoryId, PublicStory);
        roadmap.Description = "Liên hệ test@example.com";

        var result = await _sut.GetRoadmapAsync(Moderator, 1);

        Assert.Equal("Liên hệ [email]", result.Description);
        Assert.Equal(new[] { ContentReportHarness.PrivateStoryId, PublicStory },
            result.Stories.Select(story => story.StoryId));
        Assert.Equal("friendship", result.MainCategory!.Code);
        _h.Audit.Verify(audit => audit.LogAsync(
            Moderator, "PublishedRoadmapOpened", nameof(ReadingRoadmap), 1,
            null, It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ListAsync_RoadmapTemplate_FlagsOnlyRoadmapContainingArchivedStory()
    {
        _h.AddPublicStory(30, "6-8", DateTime.UtcNow.AddDays(-1));
        _h.AddRoadmap(1, AgeBand.Age_6_8, 5, DateTime.UtcNow,
            PublicStory, 30);
        _h.AddRoadmap(2, AgeBand.Age_6_8, 6, DateTime.UtcNow.AddDays(-1),
            PublicStory);
        _h.Stories.Single(story => story.Id == 30).Status = StoryStatus.Rejected;

        var result = await _sut.ListAsync(Moderator, new PublishedContentQueryDto
        {
            Source = PublishedContentSource.RoadmapTemplate
        });

        Assert.True(result.Items.Single(item => item.Id == 1).NeedsUpdate);
        Assert.False(result.Items.Single(item => item.Id == 2).NeedsUpdate);
    }

    [Fact]
    public async Task ListAsync_PublicLibrary_NeverCarriesNeedsUpdateFlag()
    {
        var result = await _sut.ListAsync(Moderator, new PublishedContentQueryDto
        {
            Source = PublishedContentSource.PublicLibrary
        });

        Assert.All(result.Items, item => Assert.False(item.NeedsUpdate));
    }

    [Fact]
    public async Task GetRoadmapAsync_ReportsNeedsUpdateAndArchivedStory()
    {
        _h.AddPublicStory(30, "6-8", DateTime.UtcNow.AddDays(-1));
        _h.AddRoadmap(1, AgeBand.Age_6_8, 5, DateTime.UtcNow, PublicStory, 30);
        var removed = _h.Stories.Single(story => story.Id == 30);
        removed.Status = StoryStatus.Rejected;
        removed.ArchivedAt = DateTime.UtcNow.AddDays(-1);

        var result = await _sut.GetRoadmapAsync(Moderator, 1);

        Assert.True(result.NeedsUpdate);
        Assert.True(result.Stories.Single(story => story.StoryId == 30).IsArchived);
    }

    [Fact]
    public async Task GetRoadmapAsync_HealthyRoadmapDoesNotNeedUpdate()
    {
        _h.AddRoadmap(1, AgeBand.Age_6_8, 5, DateTime.UtcNow, PublicStory);

        Assert.False((await _sut.GetRoadmapAsync(Moderator, 1)).NeedsUpdate);
    }

    [Fact]
    public void Controller_IsRestrictedToStaffAndOnlyModeratorsMayCreateReviewItems()
    {
        var authorize = typeof(PublishedContentReviewController).GetCustomAttribute<AuthorizeAttribute>();
        var methods = typeof(PublishedContentReviewController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(method => method.Name).OrderBy(name => name).ToArray();
        var createAuthorize = typeof(PublishedContentReviewController)
            .GetMethod("CreateReviewItem")!.GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Equal("Moderator,Administrator", authorize!.Roles);
        Assert.Equal(new[] { "CreateReviewItem", "GetRoadmap", "GetStory", "List" }, methods);
        Assert.NotNull(createAuthorize);
        Assert.Equal("Moderator", createAuthorize!.Roles);
    }
}
