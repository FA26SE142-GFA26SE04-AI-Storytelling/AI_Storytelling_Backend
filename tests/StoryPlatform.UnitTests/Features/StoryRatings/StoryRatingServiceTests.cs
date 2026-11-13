using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.StoryRatings.DTOs;
using StoryPlatform.Domain.Enums;
using Xunit;
using static StoryPlatform.UnitTests.Features.StoryRatings.StoryRatingHarness;

namespace StoryPlatform.UnitTests.Features.StoryRatings;

public sealed class StoryRatingServiceTests
{
    private readonly StoryRatingHarness _h = new();

    private Task<MyStoryRatingDto> Upsert(int userId, int rating, string? comment, int storyId = PublicStoryId) =>
        _h.Service().UpsertMineAsync(userId, storyId, new UpsertStoryRatingRequestDto { Rating = rating, Comment = comment });

    [Fact]
    public async Task Upsert_NewCleanRating_IsPublishedAndNotifiesCreatorOnce()
    {
        var created = await Upsert(ParentId, 5, "Bé rất thích");

        Assert.Equal("Published", created.Status);
        Assert.Equal("Phụ huynh H***", created.ReviewerName);
        var saved = Assert.Single(_h.Ratings);
        Assert.Equal(StoryRatingStatus.Published, saved.Status);
        Assert.Empty(_h.Items);
        var sent = Assert.Single(_h.Sent);
        Assert.Equal((CreatorId, NotificationType.StoryRatingPublished), (sent.Recipient, sent.Type));

        // Sửa một đánh giá đang hiện: vẫn 1 dòng, không báo Creator lần nữa.
        await Upsert(ParentId, 4, "Bé vẫn thích");
        Assert.Equal(4, Assert.Single(_h.Ratings).Rating);
        Assert.Single(_h.Sent);
    }

    [Fact]
    public async Task Upsert_LowRatingWithText_WaitsForModerator_WithOneScreeningItem()
    {
        var created = await Upsert(ParentId, 2, "Kết truyện hơi vội");

        Assert.Equal("PendingModeration", created.Status);
        var item = Assert.Single(_h.Items);
        Assert.Equal(ModerationItemType.StoryRating, item.ItemType);
        Assert.Equal(ModerationPriority.Low, item.Priority);
        Assert.Equal(_h.Ratings[0].Id, item.StoryRatingId);
        Assert.Equal(ParentId, item.CreatedByUserId);
        Assert.Equal(PublicStoryId, item.StoryId);
        Assert.Contains("\"source\":\"Screening\"", item.Flags);
        Assert.Contains("LOW_RATING_WITH_TEXT", item.Flags);
        Assert.Empty(_h.Sent);

        // Sửa thành 5 sao sạch khi đang chờ: vẫn chờ, không tạo mục thứ hai.
        var edited = await Upsert(ParentId, 5, "Đọc lại thấy hay");
        Assert.Equal("PendingModeration", edited.Status);
        Assert.Single(_h.Items);
    }

    [Fact]
    public async Task Upsert_EditingHiddenRating_GoesBackToModeration_EvenWhenClean()
    {
        _h.AddRating(1, ParentId, 1, StoryRatingStatus.Hidden, "cũ", hiddenReason: "Công kích");

        var edited = await Upsert(ParentId, 5, "Xin lỗi tác giả, truyện hay");

        Assert.Equal("PendingModeration", edited.Status);
        Assert.Null(edited.HiddenReason);
        Assert.Single(_h.Items);
        Assert.Empty(_h.Sent);
    }

    [Fact]
    public async Task Upsert_StarsOnly_IsPublishedWithoutComment()
    {
        var created = await Upsert(ParentId, 1, "   ");

        Assert.Equal("Published", created.Status);
        Assert.Null(created.Comment);
    }

    [Fact]
    public async Task Upsert_MasksPhoneAndEmail()
    {
        var created = await Upsert(ParentId, 5, "Liên hệ 0912345678 hoặc a@b.com");

        Assert.DoesNotContain("0912345678", created.Comment);
        Assert.Contains("[số điện thoại]", created.Comment);
        Assert.Contains("[email]", _h.Ratings[0].Comment);
    }

    [Fact]
    public async Task Upsert_ParentWhoseChildNeverRead_IsForbidden()
    {
        var error = await Assert.ThrowsAsync<ForbiddenException>(() => Upsert(StrangerParentId, 5, "Hay"));

        Assert.Equal("RATING_REQUIRES_READING", error.Message);
        Assert.Empty(_h.Ratings);
    }

    [Fact]
    public async Task Upsert_InactiveChild_DoesNotCount()
    {
        _h.Children.Single(child => child.Id == ChildId).Status = ChildProfileStatus.Suspended;

        await Assert.ThrowsAsync<ForbiddenException>(() => Upsert(ParentId, 5, "Hay"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public async Task Upsert_RatingOutOfRange_IsBadRequest(int rating)
    {
        await Assert.ThrowsAsync<BadRequestException>(() => Upsert(ParentId, rating, null));
    }

    [Fact]
    public async Task Upsert_CommentOver1000Chars_IsBadRequest()
    {
        await Assert.ThrowsAsync<BadRequestException>(() => Upsert(ParentId, 5, new string('a', 1001)));
    }

    [Theory]
    [InlineData(DraftStoryId)]
    [InlineData(FamilyStoryId)]
    [InlineData(999)]
    public async Task Upsert_NotAPublicCreatorStory_IsNotFound(int storyId)
    {
        await Assert.ThrowsAsync<NotFoundException>(() => Upsert(ParentId, 5, "Hay", storyId));
    }

    [Theory]
    [InlineData(CreatorId)]
    [InlineData(ModeratorId)]
    public async Task Upsert_NonParentRole_IsForbidden(int userId)
    {
        await Assert.ThrowsAsync<ForbiddenException>(() => Upsert(userId, 5, "Hay"));
    }

    [Fact]
    public async Task GetForStory_ShowsOnlyPublishedRatings_WithSummaryAndOwnRating()
    {
        _h.AddRating(1, OtherParentId, 4, StoryRatingStatus.Published, "Hay");
        _h.AddRating(2, ParentId, 2, StoryRatingStatus.PendingModeration, "Vội");
        _h.AddRating(3, StrangerParentId, 2, StoryRatingStatus.Published);
        _h.AddRating(4, CreatorId, 1, StoryRatingStatus.Hidden, "Ẩn");

        var list = await _h.Service().GetForStoryAsync(ParentId, PublicStoryId, new StoryRatingQueryDto { OrderBy = "Highest" });

        Assert.Equal(new[] { 1, 3 }, list.Ratings.Items.Select(rating => rating.RatingId));
        Assert.Equal("Phụ huynh M***", list.Ratings.Items[0].ReviewerName);
        Assert.Equal(3.0, list.Summary.AverageRating);
        Assert.Equal(2, list.Summary.RatingCount);
        Assert.Equal(new Dictionary<int, int> { [1] = 0, [2] = 1, [3] = 0, [4] = 1, [5] = 0 }, list.Summary.Distribution);
        Assert.Equal("PendingModeration", list.MyRating!.Status);
        Assert.True(list.CanRate);
    }

    [Fact]
    public async Task GetForStory_ParentWithoutReadingChild_CannotRate()
    {
        var list = await _h.Service().GetForStoryAsync(StrangerParentId, PublicStoryId, new StoryRatingQueryDto());

        Assert.False(list.CanRate);
        Assert.Null(list.MyRating);
        Assert.Null(list.Summary.AverageRating);
    }

    [Fact]
    public async Task Delete_SoftDeletesAndClosesOpenItem()
    {
        _h.AddRating(1, ParentId, 2, StoryRatingStatus.PendingModeration, "Vội");
        var item = _h.AddOpenItem(50, 1, ParentId);

        Assert.True(await _h.Service().DeleteMineAsync(ParentId, PublicStoryId));

        Assert.True(_h.Ratings[0].IsDeleted);
        Assert.Equal(ModerationItemStatus.Closed, item.Status);
        Assert.Equal("RatingDeleted", item.ClosedReason);
        Assert.Equal(1, item.RowVersion);
    }

    [Fact]
    public async Task Delete_WithoutRating_IsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _h.Service().DeleteMineAsync(ParentId, PublicStoryId));
    }

    [Fact]
    public async Task Delete_ThenRateAgain_CreatesANewRow()
    {
        await Upsert(ParentId, 5, "Hay");
        await _h.Service().DeleteMineAsync(ParentId, PublicStoryId);

        await Upsert(ParentId, 3, "Đọc lại");

        Assert.Equal(2, _h.Ratings.Count);
        Assert.Equal(3, _h.Ratings.Single(rating => !rating.IsDeleted).Rating);
    }
}
