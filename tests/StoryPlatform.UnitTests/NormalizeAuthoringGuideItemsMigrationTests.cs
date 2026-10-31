using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using StoryPlatform.Infrastructure.Migrations;
using StoryPlatform.Infrastructure.Persistence;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed class NormalizeAuthoringGuideItemsMigrationTests
{
    private static ApplicationDbContext CreateContext() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseNpgsql("Host=localhost;Database=metadata_only;Username=unused;Password=unused").Options);

    private static Migration Load(ApplicationDbContext db)
    {
        var assembly = db.GetService<IMigrationsAssembly>();
        var entry = assembly.Migrations.Single(x => x.Key.EndsWith("_NormalizeAuthoringGuideItems"));
        return assembly.CreateMigration(entry.Value, db.Database.ProviderName!);
    }

    [Fact]
    public void Up_creates_both_tables_before_the_backfill_and_keeps_the_legacy_column()
    {
        using var db = CreateContext();
        var operations = Load(db).UpOperations.ToList();

        var backfill = Assert.Single(operations.OfType<SqlOperation>(), x => x.Sql == NormalizeAuthoringGuideItems.BackfillSql);
        var position = operations.IndexOf(backfill);
        Assert.Equal(["authoring_guide_items", "authoring_guide_placeholders"],
            operations.Take(position).OfType<CreateTableOperation>().Select(x => x.Name).Order());
        Assert.DoesNotContain(operations, x => x is DropColumnOperation or DropTableOperation);
        var alter = Assert.Single(operations.OfType<AlterColumnOperation>(), x => x.Name == "ContentJson");
        Assert.True(alter.IsNullable);

        Assert.Contains("NOT EXISTS (SELECT 1 FROM authoring_guide_items", backfill.Sql);
        Assert.Contains("jsonb_array_elements_text(COALESCE(src.item->'placeholderDefinitions'", backfill.Sql);
        Assert.Contains("authoring guide backfill row counts do not match", backfill.Sql);
    }

    [Fact]
    public void Down_refuses_to_lose_labels_rebuilds_the_legacy_json_then_drops_the_tables()
    {
        using var db = CreateContext();
        var operations = Load(db).DownOperations.ToList();

        var guard = Assert.IsType<SqlOperation>(operations[0]);
        Assert.Equal(NormalizeAuthoringGuideItems.RollbackGuardSql, guard.Sql);
        Assert.Contains("rollback would lose placeholder labels", guard.Sql);

        var restore = Assert.Single(operations.OfType<SqlOperation>(), x => x.Sql == NormalizeAuthoringGuideItems.RollbackSql);
        Assert.Contains("'placeholderDefinitions'", restore.Sql);
        Assert.Contains("'schemaVersion', 1", restore.Sql);
        Assert.All(operations.OfType<DropTableOperation>(), x => Assert.True(operations.IndexOf(x) > operations.IndexOf(restore)));
        var alter = Assert.Single(operations.OfType<AlterColumnOperation>(), x => x.Name == "ContentJson");
        Assert.False(alter.IsNullable);
        Assert.Null(alter.DefaultValue); // '' không phải JSON hợp lệ nên jsonb không nhận làm giá trị mặc định
        Assert.True(operations.IndexOf(alter) > operations.IndexOf(restore));
    }

    [Fact]
    public void Snapshot_matches_the_model_and_the_script_runs_in_one_transaction()
    {
        using var db = CreateContext();
        Assert.False(db.Database.HasPendingModelChanges());
        var ids = db.GetService<IMigrationsAssembly>().Migrations.Keys.OrderBy(x => x).ToArray();
        var target = Array.FindIndex(ids, x => x.EndsWith("_NormalizeAuthoringGuideItems"));
        var sql = db.GetService<IMigrator>().GenerateScript(ids[target - 1], ids[target]);
        Assert.True(sql.IndexOf("CREATE TABLE authoring_guide_placeholders", StringComparison.Ordinal) <
                    sql.IndexOf("INSERT INTO authoring_guide_items", StringComparison.Ordinal));
        Assert.Contains("START TRANSACTION", sql);
        Assert.Contains("COMMIT", sql);
    }
}
