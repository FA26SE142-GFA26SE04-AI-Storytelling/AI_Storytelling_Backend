using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using StoryPlatform.Infrastructure.Persistence;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed class Age9ListeningMigrationTests
{
    [Fact]
    public void Upgrade_adds_only_the_two_nullable_age9_columns()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=metadata_only;Username=unused;Password=unused").Options);
        var assembly = db.GetService<IMigrationsAssembly>();
        var migration = assembly.CreateMigration(assembly.Migrations.Single(x =>
            x.Key.EndsWith("_AddAge9ListeningInteractions")).Value, db.Database.ProviderName!);
        Assert.Equal(["listening_interaction_options.Counterpoint", "listening_interactions.RelationsJson"],
            migration.UpOperations.OfType<AddColumnOperation>()
                .Select(operation => $"{operation.Table}.{operation.Name}").Order().ToArray());
        Assert.All(migration.UpOperations.OfType<AddColumnOperation>(), operation => Assert.True(operation.IsNullable));
        Assert.Equal(2, migration.UpOperations.Count);
        Assert.DoesNotContain(migration.UpOperations, operation => operation is DropColumnOperation or DropTableOperation or SqlOperation);
        Assert.False(db.Database.HasPendingModelChanges());
    }
}
