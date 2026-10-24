using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using StoryPlatform.Infrastructure.Persistence;
using Xunit;

namespace StoryPlatform.UnitTests;

/// <summary>Inspect migration metadata and generate SQL without opening a database.</summary>
public sealed class SplitPhaseFiveMediaJobsMigrationTests
{
    private static ApplicationDbContext Context() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseNpgsql("Host=localhost;Database=metadata_only;Username=unused;Password=unused").Options);

    private static Migration Migration(ApplicationDbContext db)
    {
        var assembly = db.GetService<IMigrationsAssembly>();
        return assembly.CreateMigration(assembly.Migrations.Single(x => x.Key.EndsWith("_SplitPhaseFiveMediaJobs")).Value,
            db.Database.ProviderName!);
    }

    [Fact]
    public void UpgradePreservesLiveAssetsAndRejectsAmbiguousLegacySelectionBeforeDroppingIndexes()
    {
        using var db = Context();
        var operations = Migration(db).UpOperations.ToList();
        var guard = Assert.IsType<SqlOperation>(operations[0]);
        Assert.Contains("ambiguous legacy media", guard.Sql);
        Assert.Contains("HAVING count(*) > 1", guard.Sql);
        var columns = operations.OfType<AddColumnOperation>().ToArray();
        Assert.Equal(8, columns.Length);
        Assert.All(columns, x => Assert.Contains(x.Table, new[] { "media_assets", "story_generation_jobs" }));
        var selected = Assert.Single(columns, x => x.Name == "IsCurrent");
        Assert.Equal(true, selected.DefaultValue);
        Assert.False(selected.IsNullable);
        Assert.All(columns.Where(x => x.Name != "IsCurrent"), x => Assert.True(x.IsNullable));
        Assert.Empty(operations.OfType<DropColumnOperation>());
        Assert.Empty(operations.OfType<DropTableOperation>());
        var indexes = operations.OfType<CreateIndexOperation>().Where(x => x.IsUnique).ToArray();
        Assert.Equal(3, indexes.Length);
        Assert.Equal(2, indexes.Count(x => x.Filter!.Contains("IsCurrent")));
        Assert.Contains(indexes, x => x.Filter!.Contains("ReplacementResolvedAt"));
    }

    [Fact]
    public void DowngradeRefusesToDiscardParallelWorkAndReplacementHistory()
    {
        using var db = Context();
        var guard = Assert.IsType<SqlOperation>(Migration(db).DownOperations[0]);
        Assert.Contains("rollback would lose media jobs or replacement history", guard.Sql);
        foreach (var field in new[] { "ParentJobId", "MediaContextId", "MediaPackageRevision", "NextAttemptAt",
                     "IsCurrent", "ReplacesMediaAssetId", "RequestedPackageRevision", "ReplacementResolvedAt" })
            Assert.Contains($"\"{field}\"", guard.Sql);
    }

    [Fact]
    public void PostgresSqlAddsColumnsBeforeBackfillAndDoesNotResetPackageConfirmationOrCompletedJobs()
    {
        using var db = Context();
        Assert.False(db.Database.HasPendingModelChanges());
        var ids = db.GetService<IMigrationsAssembly>().Migrations.Keys.OrderBy(x => x).ToArray();
        var target = Array.FindIndex(ids, x => x.EndsWith("_SplitPhaseFiveMediaJobs"));
        var sql = db.GetService<IMigrator>().GenerateScript(ids[target - 1], ids[target]);
        Assert.True(sql.IndexOf("ADD \"IsCurrent\"", StringComparison.Ordinal) < sql.IndexOf("UPDATE media_assets", StringComparison.Ordinal));
        Assert.Contains("DEFAULT TRUE", sql);
        Assert.DoesNotContain("UPDATE story_version_media_states", sql);
        Assert.DoesNotContain("UPDATE story_generation_jobs", sql);
        Assert.DoesNotContain("DELETE FROM", sql);
        Assert.Contains("START TRANSACTION", sql);
        Assert.Contains("COMMIT", sql);
    }
}
