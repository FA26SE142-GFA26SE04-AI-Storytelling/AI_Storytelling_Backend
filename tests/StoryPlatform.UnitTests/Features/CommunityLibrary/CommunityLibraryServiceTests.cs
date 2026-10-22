using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.CommunityLibrary;
using StoryPlatform.Application.Features.CommunityLibrary.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.UnitTests.Features.ContentReports;
using Xunit;

namespace StoryPlatform.UnitTests.Features.CommunityLibrary;

public class CommunityLibraryServiceTests
{
    private readonly ContentReportHarness _h = new();
    private readonly CommunityLibraryService _sut;

    private const int Story = ContentReportHarness.PrivateStoryId;
    private const int SourceChild = ContentReportHarness.ChildId;
    private const int Reader = 101;
    private const int OlderReader = 102;
    private const int Submitter = ContentReportHarness.ReporterId;

    public CommunityLibraryServiceTests()
    {
        _sut = new CommunityLibraryService(_h.UnitOfWork.Object);
        _h.AddChild(Reader, ContentReportHarness.OtherParentId, AgeBand.Age_6_8);
        _h.AddChild(OlderReader, ContentReportHarness.OtherParentId, AgeBand.Age_9_10);
        _h.ContentCategories.Add(new ContentCategory { Id = 5, Code = "friendship", DisplayName = "Tình bạn", IsActive = true });
        _h.ContentCategories.Add(new ContentCategory { Id = 6, Code = "animals", DisplayName = "Động vật", IsActive = true });
        _h.StoryCategories.Add(new StoryCategory { Id = 1, StoryId = Story, ContentCategoryId = 5 });
        _h.Versions.Single(version => version.Id == 60).Title = "Thỏ và rùa";
        _h.Stories.Single(story => story.Id == Story).Description = "Một câu chuyện vui";
        _h.Stories.Single(story => story.Id == Story).ReadingLevel = 2;
    }

    private ModerationItem Approved(int id = 80, int storyId = Story, int versionId = 60)
    {
        var item = new ModerationItem
        {
            Id = id,
            ItemType = ModerationItemType.CommunitySubmission,
            Status = ModerationItemStatus.Approved,
            StoryId = storyId,
            StoryVersionId = versionId,
            CreatedByUserId = Submitter,
            SourceChildProfileId = SourceChild,
            DecidedByUserId = 12,
            DecidedAt = DateTime.UtcNow.AddDays(-1)
        };
        _h.Items.Add(item);
        return item;
    }

    private Task<PagedResult<CommunityStoryDto>> ListAsync(
        int childId = Reader, CommunityLibraryQueryDto? query = null) =>
        _sut.ListAsync(childId, query ?? new CommunityLibraryQueryDto());

    [Fact]
    public async Task ListAsync_ShowsApprovedSubmissionsWithTheCommunityAsAuthor()
    {
        Approved();

        var row = (await ListAsync()).Items.Single();

        Assert.Equal(80, row.SubmissionId);
        Assert.Equal("Thỏ và rùa", row.Title);
        Assert.Equal("Một câu chuyện vui", row.Description);
        Assert.Equal("6-8", row.AgeBand);
        Assert.Equal(2, row.ReadingLevel);
        Assert.Equal("friendship", row.Labels.Single().Code);
        Assert.Equal("Cộng đồng", row.Author);
        Assert.NotNull(row.ApprovedAt);
    }

    [Fact]
    public void CommunityStoryDto_NeverRevealsTheSubmitterTheSourceChildOrTheReviewer()
    {
        var names = typeof(CommunityStoryDto).GetProperties().Select(p => p.Name).ToList();

        Assert.DoesNotContain(names, name => name.Equals("StoryId", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, name => name.Contains("Submitter", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, name => name.Contains("Child", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, name => name.Contains("Created", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, name => name.Contains("Moderator", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, name => name.Contains("Decided", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(ModerationItemStatus.Pending)]
    [InlineData(ModerationItemStatus.Claimed)]
    [InlineData(ModerationItemStatus.Rejected)]
    [InlineData(ModerationItemStatus.Withdrawn)]
    [InlineData(ModerationItemStatus.Removed)]
    [InlineData(ModerationItemStatus.Closed)]
    public async Task ListAsync_OnlyApprovedSubmissionsAreShown(ModerationItemStatus status)
    {
        Approved().Status = status;

        Assert.Empty((await ListAsync()).Items);
    }

    [Fact]
    public async Task ListAsync_OtherItemTypesAreNotShown()
    {
        _h.Items.Add(new ModerationItem
        {
            Id = 81,
            ItemType = ModerationItemType.PublicStoryReview,
            Status = ModerationItemStatus.Approved,
            StoryId = Story,
            StoryVersionId = 60
        });

        Assert.Empty((await ListAsync()).Items);
    }

    [Fact]
    public async Task ListAsync_NewestApprovedFirst_AndPaged()
    {
        _h.AddPublicStory(30, "6-8", null);
        _h.Stories.Single(story => story.Id == 30).Scope = StoryScope.Parent;
        _h.Versions.Add(new StoryVersion { Id = 70, StoryId = 30, VersionNo = 1, Title = "Truyện hai", IsCurrent = true });
        Approved(80).DecidedAt = DateTime.UtcNow.AddDays(-3);
        Approved(81, 30, 70).DecidedAt = DateTime.UtcNow.AddDays(-1);

        var all = await ListAsync();
        var page = await ListAsync(query: new CommunityLibraryQueryDto { PageIndex = 2, PageSize = 1 });
        var clamped = await ListAsync(query: new CommunityLibraryQueryDto { PageIndex = -2, PageSize = 1000 });

        Assert.Equal(new[] { 81, 80 }, all.Items.Select(row => row.SubmissionId).ToArray());
        Assert.Equal(2, all.TotalCount);
        Assert.Equal(80, page.Items.Single().SubmissionId);
        Assert.Equal(1, clamped.PageIndex);
        Assert.Equal(100, clamped.PageSize);
    }

    [Fact]
    public async Task ListAsync_AgeBandOfTheChildDecidesWhatTheySee()
    {
        Approved();

        Assert.Single((await ListAsync(Reader)).Items);
        Assert.Empty((await ListAsync(OlderReader)).Items);
    }

    [Fact]
    public async Task ListAsync_ChildWhoBlockedALabelOfTheStory_DoesNotSeeIt_Ac2()
    {
        Approved();
        _h.Block(Reader, 5);

        Assert.Empty((await ListAsync(Reader)).Items);
    }

    [Fact]
    public async Task ListAsync_AnotherChildsBlockListDoesNotAffectMe()
    {
        Approved();
        _h.Block(ContentReportHarness.ChildId, 5);

        Assert.Single((await ListAsync(Reader)).Items);
    }

    [Fact]
    public async Task ListAsync_StoryWithAnUnblockedLabelStaysVisible()
    {
        Approved();
        _h.Block(Reader, 6);

        Assert.Single((await ListAsync(Reader)).Items);
    }

    [Fact]
    public async Task ListAsync_PinnedVersionLongerThanTheChildsLimit_IsHidden()
    {
        Approved();
        _h.Versions.Single(version => version.Id == 60).Content = new string('a', 500);
        _h.AddPolicy(Reader, maxLength: 100);

        Assert.Empty((await ListAsync(Reader)).Items);
    }

    [Fact]
    public async Task ListAsync_ChildWithoutAPolicy_HasNoTopicOrLengthFilter()
    {
        Approved();
        _h.Versions.Single(version => version.Id == 60).Content = new string('a', 50000);

        Assert.Single((await ListAsync(Reader)).Items);
    }

    [Fact]
    public async Task ListAsync_SourceStoryArchivedOrDeleted_DisappearsFromTheLibrary()
    {
        Approved();
        _h.Stories.Single(story => story.Id == Story).Status = StoryStatus.Archived;
        Assert.Empty((await ListAsync()).Items);

        _h.Stories.Single(story => story.Id == Story).Status = StoryStatus.Ready;
        _h.Stories.Single(story => story.Id == Story).IsDeleted = true;
        Assert.Empty((await ListAsync()).Items);
    }

    [Fact]
    public async Task ListAsync_ShowsThePinnedTitleEvenAfterTheSourceIsEdited()
    {
        Approved();
        _h.Versions.Single(version => version.Id == 60).IsCurrent = false;
        _h.Versions.Add(new StoryVersion { Id = 61, StoryId = Story, VersionNo = 2, Title = "Tên mới", IsCurrent = true });

        Assert.Equal("Thỏ và rùa", (await ListAsync()).Items.Single().Title);
    }

    [Fact]
    public async Task GetAsync_ReturnsTheSameCardOrNotFoundWhenFilteredOut()
    {
        Approved();

        Assert.Equal("Thỏ và rùa", (await _sut.GetAsync(Reader, 80)).Title);
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetAsync(OlderReader, 80));
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetAsync(Reader, 999));
    }

    [Fact]
    public async Task GetAsync_RemovedSubmission_IsNotFound()
    {
        Approved().Status = ModerationItemStatus.Removed;

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetAsync(Reader, 80));
    }

    [Theory]
    [InlineData(ChildProfileStatus.Suspended)]
    [InlineData(ChildProfileStatus.Archived)]
    [InlineData(ChildProfileStatus.Deleted)]
    [InlineData(ChildProfileStatus.Draft)]
    public async Task ListAsync_ProfileNotActive_IsForbidden(ChildProfileStatus status)
    {
        _h.Children.Single(child => child.Id == Reader).Status = status;

        await Assert.ThrowsAsync<ForbiddenException>(() => ListAsync(Reader));
        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.GetAsync(Reader, 80));
    }

    [Fact]
    public async Task ListAsync_UnknownProfile_IsForbidden()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() => ListAsync(999));
    }
}
