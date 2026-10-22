using Microsoft.EntityFrameworkCore;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Infrastructure.Persistence;
using StoryPlatform.Infrastructure.Persistence.Repositories;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed class CreatorAuthoringModelTests
{
    [Fact]
    public async Task TrackedDraft_RetainsOriginalRevisionWhenUpdated()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=model_metadata_only;Username=unused;Password=unused")
            .Options;
        using var context = new ApplicationDbContext(options);
        var draft = new Story { Id = 1, AuthorUserId = 1, AuthoringRevisionToken = "original" };
        context.Attach(draft);
        var repository = new GenericRepository<Story>(context);
        // Find resolves the already-tracked row locally; no SQL or SaveChanges.
        var loaded = await repository.GetByIdAsync(1);
        Assert.Same(draft, loaded);
        loaded!.AuthoringRevisionToken = "next";
        repository.Update(loaded);
        var property = context.Entry(loaded).Property(x => x.AuthoringRevisionToken);
        Assert.Equal("original", property.OriginalValue);
        Assert.Equal("next", property.CurrentValue);
    }

    [Fact]
    public void SharedModel_HasRevisionAndDeduplicationConstraints_WithoutCreatorTables()
    {
        // Inspect EF metadata only. No connection, database creation, SQL, or migration is executed.
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=model_metadata_only;Username=unused;Password=unused")
            .Options;
        using var context = new ApplicationDbContext(options);
        var story = context.Model.FindEntityType(typeof(Story))!;
        Assert.Equal("stories", story.GetTableName());
        Assert.True(story.FindProperty(nameof(Story.AuthoringRevisionToken))!.IsConcurrencyToken);
        Assert.True(story.FindProperty(nameof(Story.ChildProfileId))!.IsNullable);
        Assert.Contains(story.GetIndexes(), x => x.IsUnique && x.Properties.Any(p => p.Name == nameof(Story.CreateOperationId)));
        var version = context.Model.FindEntityType(typeof(StoryVersion))!;
        Assert.Contains(version.GetIndexes(), x => x.IsUnique &&
            x.Properties.Select(p => p.Name).SequenceEqual([nameof(StoryVersion.StoryId), nameof(StoryVersion.VersionNo)]));
        Assert.Contains(context.Model.FindEntityType(typeof(StoryAuthoringOperation))!.GetIndexes(), x => x.IsUnique && x.Properties.Any(p => p.Name == nameof(StoryAuthoringOperation.OperationId)));
        Assert.Contains(context.Model.FindEntityType(typeof(StoryVersionAuthoringMetadata))!.GetIndexes(), x => x.IsUnique && x.Properties.Any(p => p.Name == nameof(StoryVersionAuthoringMetadata.ImportPreviewHash)));
        Assert.DoesNotContain(context.Model.GetEntityTypes(), x => x.GetTableName()!.StartsWith("creator_"));
    }
    [Fact]
    public void VersionStateIsMappedToSeparateTablesAndNavigationPredicatesRemainServerSide()
    {
        using var context = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=metadata_only;Username=unused;Password=unused").Options);
        var version = context.Model.FindEntityType(typeof(StoryVersion))!;
        Assert.Null(version.FindProperty("ReviewStatus"));
        Assert.Null(version.FindProperty("SourceDeclaration"));
        Assert.Null(version.FindProperty("MediaPackageRevision"));
        Assert.Null(version.FindProperty("OperationId"));
        foreach (var (type, table) in new[] {
            (typeof(StoryVersionAuthoringMetadata), "story_version_authoring_metadata"),
            (typeof(StoryVersionModerationState), "story_version_moderation_states"),
            (typeof(StoryVersionMediaState), "story_version_media_states") })
        {
            var state = context.Model.FindEntityType(type)!;
            Assert.True(state.IsOwned());
            Assert.Equal(table, state.GetTableName());
            Assert.Equal("StoryVersionId", Assert.Single(state.FindPrimaryKey()!.Properties).Name);
        }
        var sql = context.StoryVersions.Where(x => x.Moderation.ReviewStatus == StoryPlatform.Domain.Enums.VersionReviewStatus.Pending
            && x.MediaState.MediaPackageRevision > 0 && x.AuthoringMetadata.SourceDeclaration != "").ToQueryString();
        Assert.Contains("story_version_authoring_metadata", sql);
        Assert.Contains("story_version_moderation_states", sql);
        Assert.Contains("story_version_media_states", sql);
    }

}
