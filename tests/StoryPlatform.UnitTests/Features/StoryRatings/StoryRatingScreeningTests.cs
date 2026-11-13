using StoryPlatform.Application.Features.StoryRatings;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.StoryRatings;

public sealed class StoryRatingScreeningTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void StarsOnly_IsCleanWithoutComment(string? comment)
    {
        var result = StoryRatingScreening.Screen(1, comment);

        Assert.Equal(new StoryRatingScreeningResult(null, false, StoryRatingScreening.Clean), result);
    }

    [Fact]
    public void CleanText_IsTrimmedAndPublished()
    {
        var result = StoryRatingScreening.Screen(5, "  Bé rất thích đoạn cá heo gọi bạn  ");

        Assert.Equal(new StoryRatingScreeningResult("Bé rất thích đoạn cá heo gọi bạn", false, StoryRatingScreening.Clean), result);
    }

    [Fact]
    public void PhoneAndEmail_AreMaskedButDoNotHoldTheComment()
    {
        var result = StoryRatingScreening.Screen(5, "Gọi 0912345678 hoặc mail me@x.com nhé");

        Assert.False(result.NeedsModeration);
        Assert.DoesNotContain("0912345678", result.Comment);
        Assert.DoesNotContain("me@x.com", result.Comment);
        Assert.Contains("[số điện thoại]", result.Comment);
        Assert.Contains("[email]", result.Comment);
    }

    [Theory]
    [InlineData("Xem thêm ở https://x.io/a")]
    [InlineData("vào www.truyen.vn mà đọc")]
    [InlineData("truyenhay.com có bản hay hơn")]
    public void Links_WaitForModerator(string comment)
    {
        var result = StoryRatingScreening.Screen(5, comment);

        Assert.True(result.NeedsModeration);
        Assert.Equal(StoryRatingScreening.Link, result.ReasonCode);
    }

    [Theory]
    [InlineData("Đồ ngu học")]
    [InlineData("VCL truyện gì vậy")]
    [InlineData("FUCK")]
    [InlineData("Truyện đéo hay")] // chữ "đéo" ở dạng Unicode tách dấu (NFD)
    public void BlockedTerms_WaitForModerator_CaseAndUnicodeFormInsensitive(string comment)
    {
        var result = StoryRatingScreening.Screen(5, comment);

        Assert.True(result.NeedsModeration);
        Assert.Equal(StoryRatingScreening.BlockedTerm, result.ReasonCode);
    }

    [Fact]
    public void BlockedTerm_InsideAnotherWord_IsNotMatched()
    {
        var result = StoryRatingScreening.Screen(5, "Bé thích đoạn cuối, admin xem nhé");

        Assert.False(result.NeedsModeration);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    public void LowRatingWithText_WaitsForModerator(int rating, bool held)
    {
        var result = StoryRatingScreening.Screen(rating, "Kết truyện hơi vội");

        Assert.Equal(held, result.NeedsModeration);
        Assert.Equal(held ? StoryRatingScreening.LowRatingWithText : StoryRatingScreening.Clean, result.ReasonCode);
    }

    [Fact]
    public void LinkIsReportedBeforeLowRating()
    {
        Assert.Equal(StoryRatingScreening.Link, StoryRatingScreening.Screen(1, "www.a.vn").ReasonCode);
    }

    [Theory]
    [InlineData("Trần Thị Hoa", "parent_demo1", "Phụ huynh H***")]
    [InlineData("  đỗ  ", "x", "Phụ huynh Đ***")]
    [InlineData(null, "parent_demo1", "Phụ huynh P***")]
    [InlineData("", "", "Phụ huynh")]
    public void ReviewerName_ShowsOnlyAnInitial(string? fullName, string username, string expected)
    {
        Assert.Equal(expected, StoryRatingRules.FormatReviewerName(new UserAccount { FullName = fullName!, Username = username }));
    }

    [Fact]
    public void ReviewerName_WithoutAccount_IsPrefixOnly()
    {
        Assert.Equal("Phụ huynh", StoryRatingRules.FormatReviewerName(null));
    }

    [Theory]
    [InlineData(StoryScope.Creator, true, StoryStatus.Ready, false, false, true)]
    [InlineData(StoryScope.Parent, true, StoryStatus.Ready, false, false, false)]
    [InlineData(StoryScope.Creator, false, StoryStatus.Ready, false, false, false)]
    [InlineData(StoryScope.Creator, true, StoryStatus.Draft, false, false, false)]
    [InlineData(StoryScope.Creator, true, StoryStatus.Ready, true, false, false)]
    [InlineData(StoryScope.Creator, true, StoryStatus.Ready, false, true, false)]
    public void PublicCreatorStory_MatchesCommunityLibraryRule(
        StoryScope scope, bool published, StoryStatus status, bool archived, bool deleted, bool expected)
    {
        var story = new Story
        {
            Scope = scope, IsPublished = published, Status = status, IsDeleted = deleted,
            ArchivedAt = archived ? DateTime.UtcNow : null
        };

        Assert.Equal(expected, StoryRatingRules.IsPublicCreatorStory(story));
    }
}
