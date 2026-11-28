using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using StoryPlatform.Infrastructure.Persistence;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed class Age7ListeningMigrationTests
{
    [Fact]
    public void Upgrade_OnlyAddsColumns_AndKeepsModelInSync()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=metadata_only;Username=unused;Password=unused").Options);
        var assembly = db.GetService<IMigrationsAssembly>();
        var migration = assembly.CreateMigration(assembly.Migrations.Single(x =>
            x.Key.EndsWith("_AddAge7ListeningInteractions")).Value, db.Database.ProviderName!);
        var operations = migration.UpOperations;
        Assert.All(operations, operation => Assert.IsType<AddColumnOperation>(operation));
        Assert.DoesNotContain(operations, operation => operation is DropColumnOperation or DropTableOperation or DropIndexOperation or SqlOperation);
        var columns = operations.OfType<AddColumnOperation>().Select(x => $"{x.Table}.{x.Name}").Order().ToArray();
        Assert.Equal(["listening_interaction_options.HotspotJson", "listening_interaction_options.Role",
            "listening_interactions.AcceptedOrdersJson", "listening_interactions.HotspotJson", "listening_interactions.TargetLabel"], columns);
        // Dòng cũ phải đọc được: mọi cột mới hoặc cho phép null hoặc có giá trị mặc định.
        Assert.All(operations.OfType<AddColumnOperation>(), column => Assert.True(column.IsNullable || column.DefaultValue is not null));
        Assert.Equal("None", operations.OfType<AddColumnOperation>().Single(x => x.Name == "Role").DefaultValue);
        Assert.False(db.Database.HasPendingModelChanges());
    }
}
