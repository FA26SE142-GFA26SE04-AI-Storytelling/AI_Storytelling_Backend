using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Infrastructure.Persistence;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed class KnowledgeTreeModelTests
{
    private static ApplicationDbContext CreateContext() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseNpgsql("Host=localhost;Database=metadata_only;Username=unused;Password=unused").Options);

    [Fact]
    public void Knowledge_tree_maps_to_its_table_with_one_live_row_per_child()
    {
        using var db = CreateContext();
        var entity = db.Model.FindEntityType(typeof(KnowledgeTree))!;

        Assert.Equal("knowledge_trees", entity.GetTableName());
        var index = Assert.Single(entity.GetIndexes(), x => x.Properties.Select(p => p.Name).SequenceEqual(new[] { "ChildProfileId" }));
        Assert.True(index.IsUnique);
        Assert.Equal("\"IsDeleted\" = false", index.GetFilter());
    }

    [Fact]
    public void Knowledge_tree_counters_can_never_be_negative()
    {
        using var db = CreateContext();
        var entity = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(KnowledgeTree))!;
        var checks = entity.GetCheckConstraints().Select(c => c.Sql).ToList();

        Assert.Contains("\"ExpInvested\" >= 0", checks);
        Assert.Contains("\"CurrentTreeExp\" >= 0", checks);
        Assert.Contains("\"FloweredTreeCount\" >= 0", checks);
    }

    [Fact]
    public void Notebook_entry_has_an_optional_removed_at()
    {
        using var db = CreateContext();
        var property = db.Model.FindEntityType(typeof(VocabularyNotebookEntry))!
            .FindProperty(nameof(VocabularyNotebookEntry.RemovedAt))!;

        Assert.True(property.IsNullable);
    }
}
