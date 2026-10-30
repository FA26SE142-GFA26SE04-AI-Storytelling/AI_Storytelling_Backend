using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ContentReports.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ContentReports;

public class ReportableStoryResolverTests
{
    private readonly ContentReportHarness _h = new();
    private readonly ReportableStoryResolver _sut;

    public ReportableStoryResolverTests()
    {
        _sut = new ReportableStoryResolver(_h.UnitOfWork.Object);
    }

    private Task<StoryPlatform.Application.Features.ContentReports.Interfaces.ReportableStory> Resolve(
        int storyId, int userId = ContentReportHarness.ReporterId) => _sut.ResolveAsync(userId, storyId);

    [Fact]
    public async Task ResolveAsync_PublicStoryFittingChild_ReturnsLatestApprovedVersion()
    {
        _h.Versions.Add(new StoryVersion
        {
            Id = 52,
            StoryId = ContentReportHarness.PublicStoryId,
            VersionNo = 3,
            Title = "v3",
            Moderation = new StoryVersionModerationState
            {
                ReviewStatus = VersionReviewStatus.Approved,
                ReviewedAt = DateTime.UtcNow.AddDays(-1)
            }
        });

        var result = await Resolve(ContentReportHarness.PublicStoryId);

        Assert.Equal(ContentReportHarness.PublicStoryId, result.Story.Id);
        Assert.Equal(52, result.DisplayedVersionId);
    }

    [Fact]
    public async Task ResolveAsync_PublicStoryWithoutApprovedVersion_ReturnsNullVersion()
    {
        _h.Versions.RemoveAll(version => version.Id == 50);

        Assert.Null((await Resolve(ContentReportHarness.PublicStoryId)).DisplayedVersionId);
    }

    [Fact]
    public async Task ResolveAsync_OlderChildCanAccessYoungerStory()
    {
        _h.Children.Single().AgeBand = AgeBand.Age_9_10;

        Assert.Equal(ContentReportHarness.PublicStoryId,
            (await Resolve(ContentReportHarness.PublicStoryId)).Story.Id);
    }

    [Theory]
    [InlineData("9-10")]
    [InlineData("12-14")]
    public async Task ResolveAsync_StoryOutsideChildAgeAccess_IsForbidden(string ageBand)
    {
        _h.Stories.Single(story => story.Id == ContentReportHarness.PublicStoryId).AgeBand = ageBand;

        await Assert.ThrowsAsync<ForbiddenException>(() => Resolve(ContentReportHarness.PublicStoryId));
    }

    [Fact]
    public async Task ResolveAsync_OneOfSeveralChildrenFits_IsAllowed()
    {
        _h.Stories.Single(story => story.Id == ContentReportHarness.PublicStoryId).AgeBand = "9-10";
        _h.AddChild(101, ContentReportHarness.ReporterId, AgeBand.Age_9_10);

        Assert.Equal(ContentReportHarness.PublicStoryId,
            (await Resolve(ContentReportHarness.PublicStoryId)).Story.Id);
    }

    [Fact]
    public async Task ResolveAsync_PrivateStoryOfSupervisedChild_ReturnsCurrentVersion()
    {
        Assert.Equal(60, (await Resolve(ContentReportHarness.PrivateStoryId)).DisplayedVersionId);
    }

    [Fact]
    public async Task ResolveAsync_PrivateStoryOfAnotherChild_IsForbidden()
    {
        _h.Stories.Single(story => story.Id == ContentReportHarness.PrivateStoryId).ChildProfileId = 999;

        await Assert.ThrowsAsync<ForbiddenException>(() => Resolve(ContentReportHarness.PrivateStoryId));
    }

    [Fact]
    public async Task ResolveAsync_ParentWithoutChildAccess_IsForbidden()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            Resolve(ContentReportHarness.PublicStoryId, ContentReportHarness.OtherParentId));
    }

    [Fact]
    public async Task ResolveAsync_ChangedOwner_IsForbidden()
    {
        _h.Children.Single().OwnerUserId = ContentReportHarness.OtherParentId;

        await Assert.ThrowsAsync<ForbiddenException>(() => Resolve(ContentReportHarness.PublicStoryId));
    }

    [Theory]
    [InlineData(ChildProfileStatus.Draft)]
    [InlineData(ChildProfileStatus.Suspended)]
    [InlineData(ChildProfileStatus.Archived)]
    [InlineData(ChildProfileStatus.Deleted)]
    public async Task ResolveAsync_InactiveChild_IsForbidden(ChildProfileStatus status)
    {
        _h.Children.Single().Status = status;

        await Assert.ThrowsAsync<ForbiddenException>(() => Resolve(ContentReportHarness.PublicStoryId));
    }

    [Fact]
    public async Task ResolveAsync_UnpublishedStory_IsForbidden()
    {
        _h.Stories.Single(story => story.Id == ContentReportHarness.PublicStoryId).IsPublished = false;

        await Assert.ThrowsAsync<ForbiddenException>(() => Resolve(ContentReportHarness.PublicStoryId));
    }

    [Fact]
    public async Task ResolveAsync_ArchivedStory_IsForbidden()
    {
        _h.Stories.Single(story => story.Id == ContentReportHarness.PublicStoryId).Status = StoryStatus.Archived;

        await Assert.ThrowsAsync<ForbiddenException>(() => Resolve(ContentReportHarness.PublicStoryId));
    }

    [Fact]
    public async Task ResolveAsync_StoryWithArchivedAt_IsForbidden()
    {
        _h.Stories.Single(story => story.Id == ContentReportHarness.PublicStoryId).ArchivedAt = DateTime.UtcNow;

        await Assert.ThrowsAsync<ForbiddenException>(() => Resolve(ContentReportHarness.PublicStoryId));
    }

    [Fact]
    public async Task ResolveAsync_UnknownOrDeletedStory_IsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => Resolve(9999));

        _h.Stories.Single(story => story.Id == ContentReportHarness.PublicStoryId).IsDeleted = true;
        await Assert.ThrowsAsync<NotFoundException>(() => Resolve(ContentReportHarness.PublicStoryId));
    }
}
