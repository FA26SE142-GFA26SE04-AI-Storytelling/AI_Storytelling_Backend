using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.StoryRatings.DTOs;
using StoryPlatform.Domain.Enums;
using Xunit;
using static StoryPlatform.UnitTests.Features.StoryRatings.StoryRatingHarness;

namespace StoryPlatform.UnitTests.Features.StoryRatings;

public sealed class StoryRatingReportTests
{
    private readonly StoryRatingHarness _h = new();

    private Task<StoryRatingReportResultDto> Report(int userId, int ratingId,
        StoryRatingReportReason reason = StoryRatingReportReason.Offensive, string? note = null) =>
        _h.Service().ReportAsync(userId, ratingId, new ReportStoryRatingRequestDto { Reason = reason, Note = note });

    [Fact]
    public async Task Report_PublishedRating_OpensOneItem_AndLaterReportsJoinIt()
    {
        _h.AddRating(1, ParentId, 5, StoryRatingStatus.Published, "Hay");

        var result = await Report(OtherParentId, 1, StoryRatingReportReason.Offensive, "Công kích, gọi 0912345678");
        await Report(CreatorId, 1, StoryRatingReportReason.Spam);

        Assert.Equal("Received", result.Status);
        Assert.Equal(1, result.RatingId);
        var item = Assert.Single(_h.Items);
        Assert.Equal(ModerationItemType.StoryRating, item.ItemType);
        Assert.Equal(1, item.StoryRatingId);
        Assert.Equal(ParentId, item.CreatedByUserId);
        Assert.Contains("\"source\":\"Report\"", item.Flags);
        Assert.Equal(2, _h.Reports.Count);
        Assert.All(_h.Reports, report =>
        {
            Assert.Equal(item.Id, report.ModerationItemId);
            Assert.Equal(StoryRatingReportStatus.Open, report.Status);
        });
        Assert.Contains("[số điện thoại]", _h.Reports[0].Note);
        Assert.Equal(StoryRatingStatus.Published, _h.Ratings[0].Status);
    }

    [Fact]
    public async Task Report_Twice_IsConflict()
    {
        _h.AddRating(1, ParentId, 5, StoryRatingStatus.Published, "Hay");
        await Report(OtherParentId, 1);

        await Assert.ThrowsAsync<ConflictException>(() => Report(OtherParentId, 1, StoryRatingReportReason.Spam));
        Assert.Single(_h.Reports);
    }

    [Fact]
    public async Task Report_OwnRating_IsBadRequest()
    {
        _h.AddRating(1, ParentId, 5, StoryRatingStatus.Published, "Hay");

        await Assert.ThrowsAsync<BadRequestException>(() => Report(ParentId, 1));
    }

    [Theory]
    [InlineData(StoryRatingStatus.PendingModeration)]
    [InlineData(StoryRatingStatus.Hidden)]
    public async Task Report_RatingNotShown_IsBadRequest(StoryRatingStatus status)
    {
        _h.AddRating(1, ParentId, 2, status, "Vội");

        await Assert.ThrowsAsync<BadRequestException>(() => Report(OtherParentId, 1));
    }

    [Fact]
    public async Task Report_CreatorOfAnotherStory_IsForbidden()
    {
        _h.AddRating(1, ParentId, 5, StoryRatingStatus.Published, "Hay");

        await Assert.ThrowsAsync<ForbiddenException>(() => Report(OtherCreatorId, 1));
    }

    [Theory]
    [InlineData(ModeratorId)]
    [InlineData(AdminId)]
    public async Task Report_StaffRole_IsForbidden(int userId)
    {
        _h.AddRating(1, ParentId, 5, StoryRatingStatus.Published, "Hay");

        await Assert.ThrowsAsync<ForbiddenException>(() => Report(userId, 1));
    }

    [Fact]
    public async Task Report_UnknownOrDeletedRating_IsNotFound()
    {
        _h.AddRating(1, ParentId, 5, StoryRatingStatus.Published, "Hay").IsDeleted = true;

        await Assert.ThrowsAsync<NotFoundException>(() => Report(OtherParentId, 1));
        await Assert.ThrowsAsync<NotFoundException>(() => Report(OtherParentId, 99));
    }

    [Fact]
    public async Task Report_InvalidReasonOrLongNote_IsBadRequest()
    {
        _h.AddRating(1, ParentId, 5, StoryRatingStatus.Published, "Hay");

        await Assert.ThrowsAsync<BadRequestException>(() => Report(OtherParentId, 1, (StoryRatingReportReason)99));
        await Assert.ThrowsAsync<BadRequestException>(() =>
            Report(OtherParentId, 1, StoryRatingReportReason.Other, new string('a', 501)));
    }

    [Fact]
    public async Task CreatorList_OwnStory_ShowsOnlyPublishedRatings()
    {
        _h.AddRating(1, ParentId, 5, StoryRatingStatus.Published, "Hay");
        _h.AddRating(2, OtherParentId, 1, StoryRatingStatus.PendingModeration, "Vội");

        var list = await _h.Service().GetForCreatorAsync(CreatorId, PublicStoryId, new StoryRatingQueryDto());

        Assert.Equal(1, Assert.Single(list.Ratings.Items).RatingId);
        Assert.Equal(1, list.Summary.RatingCount);
        Assert.Null(list.MyRating);
        Assert.False(list.CanRate);
    }

    [Fact]
    public async Task CreatorList_DraftOwnStory_IsAllowed_OtherCreatorsStory_IsNotFound()
    {
        var draft = await _h.Service().GetForCreatorAsync(CreatorId, DraftStoryId, new StoryRatingQueryDto());
        Assert.Empty(draft.Ratings.Items);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _h.Service().GetForCreatorAsync(OtherCreatorId, PublicStoryId, new StoryRatingQueryDto()));
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _h.Service().GetForCreatorAsync(ParentId, PublicStoryId, new StoryRatingQueryDto()));
    }
}
