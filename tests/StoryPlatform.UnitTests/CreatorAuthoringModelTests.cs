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
        Assert.Contains(version.GetIndexes(), x => x.IsUnique && x.Properties.Any(p => p.Name == nameof(StoryVersion.OperationId)));
        Assert.Contains(version.GetIndexes(), x => x.IsUnique && x.Properties.Any(p => p.Name == nameof(StoryVersion.ImportPreviewHash)));
        Assert.DoesNotContain(context.Model.GetEntityTypes(), x => x.GetTableName()!.StartsWith("creator_"));
    }
}
