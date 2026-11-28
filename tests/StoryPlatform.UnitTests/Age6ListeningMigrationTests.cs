using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using StoryPlatform.Infrastructure.Persistence;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed class Age6ListeningMigrationTests
{
    [Fact]
    public void Upgrade_PreservesDataAndScopesAnchorsAndJobsByAge()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=metadata_only;Username=unused;Password=unused").Options);
        var assembly = db.GetService<IMigrationsAssembly>();
        var migration = assembly.CreateMigration(assembly.Migrations.Single(x =>
            x.Key.EndsWith("_AddAge6ListeningInteractions")).Value, db.Database.ProviderName!);
        var operations = migration.UpOperations;
        var columns = operations.OfType<AddColumnOperation>().ToArray();
        Assert.Equal(["BlankIndex", "StepResultsJson", "TargetAge", "WordStatusesJson"], columns.Select(x => x.Name).Order());
        Assert.All(columns, column => Assert.True(column.IsNullable));
        Assert.DoesNotContain(operations, operation => operation is DropColumnOperation or DropTableOperation or SqlOperation);
        var indexes = operations.OfType<CreateIndexOperation>().ToArray();
        Assert.Equal(4, indexes.Length);
        Assert.All(indexes.Where(x => x.IsUnique && x.Table == "listening_interactions"),
            index => Assert.Contains("TargetAge", index.Columns));
        Assert.Contains("OperationKey", Assert.Single(indexes, x => x.Table == "story_generation_jobs").Columns);
        Assert.False(db.Database.HasPendingModelChanges());
    }
}
