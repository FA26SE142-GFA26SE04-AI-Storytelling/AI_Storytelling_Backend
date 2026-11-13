using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Infrastructure.Persistence;
using Xunit;

namespace StoryPlatform.UnitTests.Features.StoryRatings;

/// <summary>Mapping EF của đánh giá truyện (chỉ đọc metadata, không mở kết nối database).</summary>
public sealed class StoryRatingModelTests
{
    private static ApplicationDbContext Context() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseNpgsql("Host=localhost;Database=metadata_only;Username=unused;Password=unused").Options);

    [Fact]
    public void StoryRatings_AllowOneLiveRatingPerParentAndStory()
    {
        using var context = Context();
        var rating = context.Model.FindEntityType(typeof(StoryRating))!;

        Assert.Equal("story_ratings", rating.GetTableName());
        var unique = Assert.Single(rating.GetIndexes(), index => index.IsUnique);
        Assert.Equal(new[] { "StoryId", "ReviewerUserId" }, unique.Properties.Select(p => p.Name));
        Assert.Equal("NOT \"IsDeleted\"", unique.GetFilter());
        // Check constraint chỉ đọc được từ design-time model.
        var designRating = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(StoryRating))!;
        Assert.Contains(designRating.GetCheckConstraints(), check => check.Sql == "\"Rating\" BETWEEN 1 AND 5");
        Assert.Equal(1000, rating.FindProperty(nameof(StoryRating.Comment))!.GetMaxLength());
    }

    [Fact]
    public void StoryRatingReports_AllowOneReportPerPersonAndRating()
    {
        using var context = Context();
        var report = context.Model.FindEntityType(typeof(StoryRatingReport))!;

        Assert.Equal("story_rating_reports", report.GetTableName());
        Assert.Contains(report.GetIndexes(), index => index.IsUnique
            && index.Properties.Select(p => p.Name).SequenceEqual(new[] { "StoryRatingId", "ReporterUserId" }));
        Assert.Equal(500, report.FindProperty(nameof(StoryRatingReport.Note))!.GetMaxLength());
    }

    [Fact]
    public void ModerationItem_AllowsOneOpenItemPerStoryRating()
    {
        using var context = Context();
        var item = context.Model.FindEntityType(typeof(ModerationItem))!;

        var index = Assert.Single(item.GetIndexes(), index => index.IsUnique
            && index.Properties.Select(p => p.Name).SequenceEqual(new[] { "StoryRatingId" }));
        Assert.Equal(
            "\"ItemType\" = 'StoryRating' AND \"Status\" IN ('Pending', 'Claimed') AND NOT \"IsDeleted\"",
            index.GetFilter());
    }
}
