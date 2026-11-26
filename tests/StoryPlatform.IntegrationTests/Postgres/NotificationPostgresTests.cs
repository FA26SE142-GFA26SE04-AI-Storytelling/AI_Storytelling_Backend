using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.Infrastructure.Persistence;
using Xunit;

namespace StoryPlatform.IntegrationTests.Postgres;

// Opt-in only (STORYPLATFORM_TEST_PG). Migration dữ liệu viết bằng SQL của PostgreSQL.
public sealed class NotificationPostgresTests(AuthoringGuidePostgresFixture fixture) : IClassFixture<AuthoringGuidePostgresFixture>
{
    [PostgresFact]
    public async Task Migration_SoftDeletesRetiredTypes_AndKeepsEveryCurrentType()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var db = new ApplicationDbContext(database.Options);
        var migrator = db.GetService<IMigrator>();
        var migrations = db.Database.GetMigrations().ToList();
        await migrator.MigrateAsync(migrations[migrations.FindIndex(id => id.EndsWith("_SoftDeleteRetiredNotifications")) - 1]);

        // Chỉ các loại có lúc viết migration; loại thêm sau không tồn tại khi migration này chạy.
        var types = Enum.GetValues<NotificationType>().Where(type => type <= NotificationType.EventAwardApproved).ToArray();
        db.AddRange(types.Select(type => new Notification { RecipientUserId = database.ParentUserId, Type = type }));
        await db.SaveChangesAsync();
        foreach (var retired in new[] { "HoldModeAlert", "StoryShared" })
        {
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO notifications ("RecipientUserId", "Type", "Status", "CreatedAt", "IsDeleted")
                VALUES ({database.ParentUserId}, {retired}, 'Unread', NOW(), FALSE)
                """);
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.Notifications.AsNoTracking().ToListAsync());

        await migrator.MigrateAsync();

        var kept = await db.Notifications.AsNoTracking().Select(item => item.Type).ToListAsync();
        Assert.Equal(types.Order(), kept.Order());
        Assert.Equal(types.Length + 2, await db.Notifications.IgnoreQueryFilters().CountAsync());
    }
}
