using Microsoft.EntityFrameworkCore;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Infrastructure.Persistence;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed class StorySummaryModelTests
{
    private static ApplicationDbContext CreateContext() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseNpgsql("Host=localhost;Database=metadata_only;Username=unused;Password=unused").Options);

    [Fact]
    public void Story_summary_maps_to_its_table_with_one_live_row_per_reading_session()
    {
        using var db = CreateContext();
        var entity = db.Model.FindEntityType(typeof(StorySummary))!;

        Assert.Equal("story_summaries", entity.GetTableName());
        var index = Assert.Single(entity.GetIndexes(), x => x.Properties.Select(p => p.Name).SequenceEqual(new[] { "ReadingSessionId" }));
        Assert.True(index.IsUnique);
        Assert.Equal("\"IsDeleted\" = false", index.GetFilter());
        Assert.Equal(500, entity.FindProperty(nameof(StorySummary.AudioStoragePath))!.GetMaxLength());
    }

    [Fact]
    public void Story_summary_flag_is_unique_per_summary_and_scene()
    {
        using var db = CreateContext();
        var entity = db.Model.FindEntityType(typeof(StorySummaryFlag))!;

        Assert.Equal("story_summary_flags", entity.GetTableName());
        var index = Assert.Single(entity.GetIndexes(), x => x.IsUnique);
        Assert.Equal(new[] { "StorySummaryId", "StorySceneId" }, index.Properties.Select(p => p.Name));
    }
}
