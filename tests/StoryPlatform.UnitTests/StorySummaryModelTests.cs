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
    public void Story_summary_questions_are_unique_per_scene_or_selected_picture()
    {
        using var db = CreateContext();
        var entity = db.Model.FindEntityType(typeof(StorySummaryFlag))!;

        Assert.Equal("story_summary_flags", entity.GetTableName());
        var indexes = entity.GetIndexes().Where(x => x.IsUnique).ToArray();
        Assert.Equal(2, indexes.Length);
        var sceneIndex = Assert.Single(indexes, x => x.Properties.Select(p => p.Name)
            .SequenceEqual(new[] { "StorySummaryId", "StorySceneId" }));
        Assert.Equal("\"IllustrationBeatId\" IS NULL", sceneIndex.GetFilter());
        var beatIndex = Assert.Single(indexes, x => x.Properties.Select(p => p.Name)
            .SequenceEqual(new[] { "StorySummaryId", "IllustrationBeatId" }));
        Assert.Equal("\"IllustrationBeatId\" IS NOT NULL", beatIndex.GetFilter());
        Assert.Equal(200, entity.FindProperty(nameof(StorySummaryFlag.QuestionText))!.GetMaxLength());
        Assert.Equal(500, entity.FindProperty(nameof(StorySummaryFlag.AudioStoragePath))!.GetMaxLength());
        Assert.Equal(50, entity.FindProperty(nameof(StorySummaryFlag.AudioContentType))!.GetMaxLength());
        Assert.Equal(DeleteBehavior.Restrict, Assert.Single(entity.GetForeignKeys(),
            x => x.Properties.Single().Name == nameof(StorySummaryFlag.IllustrationBeatId)).DeleteBehavior);
        Assert.Equal(DeleteBehavior.SetNull, Assert.Single(entity.GetForeignKeys(),
            x => x.Properties.Single().Name == nameof(StorySummaryFlag.MediaAssetId)).DeleteBehavior);
    }
}
