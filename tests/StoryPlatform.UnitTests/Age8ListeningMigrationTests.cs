using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using StoryPlatform.Infrastructure.Persistence;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed class Age8ListeningMigrationTests
{
    private static ApplicationDbContext Db() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseNpgsql("Host=localhost;Database=metadata_only;Username=unused;Password=unused").Options);

    [Fact]
    public void Upgrade_AddsOnlyNullableColumns_AndRebuildsThePracticeIndexWithNullsNotDistinct()
    {
        using var db = Db();
        var assembly = db.GetService<IMigrationsAssembly>();
        var migration = assembly.CreateMigration(assembly.Migrations.Single(x =>
            x.Key.EndsWith("_AddAge8ListeningInteractions")).Value, db.Database.ProviderName!);
        var operations = migration.UpOperations;

        var columns = operations.OfType<AddColumnOperation>().Select(x => $"{x.Table}.{x.Name}").Order().ToArray();
        Assert.Equal(["listening_interaction_options.Emotion", "listening_interaction_options.Speaker",
            "read_aloud_recordings.ListeningInteractionOptionId"], columns);
        Assert.All(operations.OfType<AddColumnOperation>(), column => Assert.True(column.IsNullable));
        Assert.DoesNotContain(operations, operation => operation is DropColumnOperation or DropTableOperation or SqlOperation);

        // Chỉ đúng một index được dựng lại: unique của đoạn luyện, tính NULL là một giá trị để các đoạn luyện cũ vẫn bị chặn trùng.
        var dropped = Assert.Single(operations.OfType<DropIndexOperation>());
        Assert.Equal("UX_read_aloud_recordings_session_practice", dropped.Name);
        var created = Assert.Single(operations.OfType<CreateIndexOperation>());
        Assert.Equal("UX_read_aloud_recordings_session_practice", created.Name);
        Assert.True(created.IsUnique);
        Assert.Equal(["ReadingSessionId", "ListeningInteractionId", "ListeningInteractionOptionId"], created.Columns);
        Assert.Equal(false, created["Npgsql:NullsDistinct"]);
        Assert.False(db.Database.HasPendingModelChanges());
    }
}
