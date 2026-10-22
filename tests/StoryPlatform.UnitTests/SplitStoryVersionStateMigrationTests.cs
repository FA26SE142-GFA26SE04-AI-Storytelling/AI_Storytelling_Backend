using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using StoryPlatform.Infrastructure.Persistence;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed class SplitStoryVersionStateMigrationTests
{
    private static ApplicationDbContext CreateContext() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseNpgsql("Host=localhost;Database=metadata_only;Username=unused;Password=unused").Options);

    [Fact]
    public void BackfillPrecedesColumnRemovalAndIncludesDeletedVersionsAndReceipts()
    {
        using var db = CreateContext();
        var assembly = db.GetService<IMigrationsAssembly>();
        var entry = assembly.Migrations.Single(x => x.Key.EndsWith("_SplitStoryVersionState"));
        var migration = assembly.CreateMigration(entry.Value, db.Database.ProviderName!);
        var operations = migration.UpOperations.ToList();
        var backfill = Assert.Single(operations.OfType<SqlOperation>(), x => x.Sql.Contains("INSERT INTO story_version_authoring_metadata"));
        var position = operations.IndexOf(backfill);
        Assert.Equal(4, operations.Take(position).OfType<CreateTableOperation>().Count());
        Assert.All(operations.OfType<DropColumnOperation>(), x => Assert.True(operations.IndexOf(x) > position));
        Assert.DoesNotContain("WHERE NOT", backfill.Sql);
        Assert.DoesNotContain("WHERE \"IsDeleted\"", backfill.Sql);
        Assert.Contains("v.\"IsDeleted\"", backfill.Sql);
        Assert.Contains("COALESCE(v.\"EditorUserId\", s.\"AuthorUserId\")", backfill.Sql);
        Assert.Contains("v.\"RequestHash\", v.\"OperationResponseJson\"", backfill.Sql);
        Assert.Contains("WHEN 'Import' THEN 'confirm-import'", backfill.Sql);
        Assert.Contains("WHEN 'Restore' THEN 'restore'", backfill.Sql);
        Assert.Contains("backfill row counts do not match", backfill.Sql);
        Assert.Contains("to_jsonb(v)->>'ConfirmedMediaPackageRevision'", backfill.Sql);
    }

    [Fact]
    public void RollbackRestoresLegacyFieldsBeforeDroppingTablesAndRejectsLossyDowngrade()
    {
        using var db = CreateContext();
        var assembly = db.GetService<IMigrationsAssembly>();
        var entry = assembly.Migrations.Single(x => x.Key.EndsWith("_SplitStoryVersionState"));
        var migration = assembly.CreateMigration(entry.Value, db.Database.ProviderName!);
        var operations = migration.DownOperations.ToList();
        var guard = Assert.IsType<SqlOperation>(operations[0]);
        Assert.Contains("HAVING count(*) > 1", guard.Sql);
        Assert.Contains("rollback would lose receipts or media confirmation", guard.Sql);
        var restore = Assert.Single(operations.OfType<SqlOperation>(), x => x.Sql.Contains("UPDATE story_versions AS v"));
        Assert.All(operations.OfType<DropTableOperation>(), x => Assert.True(operations.IndexOf(x) > operations.IndexOf(restore)));
        Assert.All(operations.OfType<AddColumnOperation>(), x => Assert.True(operations.IndexOf(x) < operations.IndexOf(restore)));
        Assert.Contains("\"OperationResponseJson\" = o.\"OperationResponseJson\"", restore.Sql);
    }

    [Fact]
    public void SnapshotMatchesModelAndPostgresScriptPreservesBackfillOrdering()
    {
        using var db = CreateContext();
        Assert.False(db.Database.HasPendingModelChanges());
        var assembly = db.GetService<IMigrationsAssembly>();
        var ids = assembly.Migrations.Keys.OrderBy(x => x).ToArray();
        var target = Array.FindIndex(ids, x => x.EndsWith("_SplitStoryVersionState"));
        var sql = db.GetService<IMigrator>().GenerateScript(ids[target - 1], ids[target]);
        Assert.True(sql.IndexOf("CREATE TABLE story_authoring_operations", StringComparison.Ordinal) <
                    sql.IndexOf("INSERT INTO story_authoring_operations", StringComparison.Ordinal));
        Assert.True(sql.IndexOf("INSERT INTO story_version_moderation_states", StringComparison.Ordinal) <
                    sql.IndexOf("DROP COLUMN \"ReviewStatus\"", StringComparison.Ordinal));
        Assert.Contains("START TRANSACTION", sql);
        Assert.Contains("COMMIT", sql);
    }
}
