using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.Infrastructure.Persistence;
using Xunit;

namespace StoryPlatform.IntegrationTests;

public sealed class ChildRoadmapSchemaModelTests
{
    [Fact]
    public void Enums_match_the_workflow_documents()
    {
        Assert.Equal(
            new[] { "Waiting", "Open", "Completed", "Skipped", "Removed" },
            Enum.GetNames<ChildRoadmapItemStatus>());
        Assert.Equal(
            new[] { "BlockedTopic", "WrongAge", "TooLong", "StoryUnavailable", "SourceDeleted", "DataDeleted" },
            Enum.GetNames<ChildRoadmapSkipReason>());
        Assert.Equal(new[] { "Manual", "TemplateCopy", "Wish" }, Enum.GetNames<ChildRoadmapItemSource>());
    }

    [Fact]
    public void ChildRoadmapItem_maps_to_child_roadmap_items_with_string_enums()
    {
        using var context = CreateModelOnlyContext();
        var entity = context.Model.FindEntityType(typeof(ChildRoadmapItem))!;

        Assert.Equal("child_roadmap_items", entity.GetTableName());
        Assert.Equal(30, entity.FindProperty(nameof(ChildRoadmapItem.Status))!.GetMaxLength());
        Assert.Equal(ChildRoadmapItemStatus.Waiting, entity.FindProperty(nameof(ChildRoadmapItem.Status))!.GetDefaultValue());
        Assert.Equal(30, entity.FindProperty(nameof(ChildRoadmapItem.SkipReason))!.GetMaxLength());
        Assert.Equal(30, entity.FindProperty(nameof(ChildRoadmapItem.Source))!.GetMaxLength());
        Assert.Equal(500, entity.FindProperty(nameof(ChildRoadmapItem.Note))!.GetMaxLength());
        Assert.False(entity.FindProperty(nameof(ChildRoadmapItem.ChildProfileId))!.IsNullable);
        Assert.False(entity.FindProperty(nameof(ChildRoadmapItem.StoryId))!.IsNullable);
        Assert.True(entity.FindProperty(nameof(ChildRoadmapItem.SkipReason))!.IsNullable);
    }

    [Fact]
    public void ChildRoadmapItem_enforces_one_open_step_per_child_and_no_duplicate_unfinished_story()
    {
        using var context = CreateModelOnlyContext();
        var entity = context.Model.FindEntityType(typeof(ChildRoadmapItem))!;

        var openPerChild = entity.GetIndexes().Single(index =>
            index.IsUnique
            && index.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(ChildRoadmapItem.ChildProfileId) }));
        Assert.Contains("'Open'", openPerChild.GetFilter());

        var uniqueStory = entity.GetIndexes().Single(index =>
            index.IsUnique
            && index.Properties.Select(p => p.Name)
                .SequenceEqual(new[] { nameof(ChildRoadmapItem.ChildProfileId), nameof(ChildRoadmapItem.StoryId) }));
        Assert.Contains("'Waiting'", uniqueStory.GetFilter());
        Assert.Contains("'Open'", uniqueStory.GetFilter());
    }

    [Fact]
    public void ChildRoadmapItem_uses_restrict_foreign_keys_and_soft_delete_filter()
    {
        using var context = CreateModelOnlyContext();
        var entity = context.Model.FindEntityType(typeof(ChildRoadmapItem))!;

        Assert.All(entity.GetForeignKeys(), key => Assert.Equal(DeleteBehavior.Restrict, key.DeleteBehavior));
        var targets = entity.GetForeignKeys().Select(key => key.PrincipalEntityType.ClrType).ToHashSet();
        Assert.Contains(typeof(ChildProfile), targets);
        Assert.Contains(typeof(Story), targets);
        Assert.Contains(typeof(ReadingRoadmap), targets);
        Assert.Contains(typeof(UserAccount), targets);
        Assert.NotNull(entity.GetQueryFilter());
    }

    [Fact]
    public void ReadingSession_links_to_the_roadmap_step_it_completes()
    {
        using var context = CreateModelOnlyContext();
        var session = context.Model.FindEntityType(typeof(ReadingSession))!;

        var property = session.FindProperty(nameof(ReadingSession.ChildRoadmapItemId))!;
        Assert.True(property.IsNullable);
        var key = session.GetForeignKeys().Single(foreignKey => foreignKey.Properties.Contains(property));
        Assert.Equal(typeof(ChildRoadmapItem), key.PrincipalEntityType.ClrType);
        Assert.Equal(DeleteBehavior.Restrict, key.DeleteBehavior);
    }

    private static ApplicationDbContext CreateModelOnlyContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=model_only;Password=model_only")
            .Options);
}
