using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using StoryPlatform.Infrastructure.Migrations;
using StoryPlatform.Infrastructure.Persistence;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed class ReplaceStoryArchivedWithRejectedMigrationTests
{
    private static ApplicationDbContext CreateContext() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseNpgsql("Host=localhost;Database=metadata_only;Username=unused;Password=unused").Options);

    private static Migration Load(ApplicationDbContext db)
    {
        var assembly = db.GetService<IMigrationsAssembly>();
        var entry = assembly.Migrations.Single(x => x.Key.EndsWith("_ReplaceStoryArchivedWithRejected"));
        return assembly.CreateMigration(entry.Value, db.Database.ProviderName!);
    }

    [Fact]
    public void Up_only_moves_archived_stories_to_rejected()
    {
        using var db = CreateContext();
        var sql = Assert.IsType<SqlOperation>(Assert.Single(Load(db).UpOperations));

        Assert.Equal(ReplaceStoryArchivedWithRejected.UpSql, sql.Sql);
        Assert.Contains("UPDATE stories SET \"Status\" = 'Rejected'", sql.Sql);
        Assert.Contains("WHERE \"Status\" = 'Archived'", sql.Sql);
    }

    [Fact]
    public void Down_restores_only_rows_with_removal_metadata()
    {
        using var db = CreateContext();
        var sql = Assert.IsType<SqlOperation>(Assert.Single(Load(db).DownOperations));

        Assert.Equal(ReplaceStoryArchivedWithRejected.DownSql, sql.Sql);
        Assert.Contains("UPDATE stories SET \"Status\" = 'Archived'", sql.Sql);
        Assert.Contains("\"ArchivedAt\" IS NOT NULL OR \"ArchivedReason\" IS NOT NULL", sql.Sql);
    }
}
