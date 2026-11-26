using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.Infrastructure.Persistence;
using Xunit;

namespace StoryPlatform.IntegrationTests.Postgres;

// Opt-in only (STORYPLATFORM_TEST_PG). Migration dữ liệu viết bằng SQL của PostgreSQL.
public sealed class ContentCategoryPostgresTests(AuthoringGuidePostgresFixture fixture) : IClassFixture<AuthoringGuidePostgresFixture>
{
    private const string Migration = "_ImportMissingContentCategories";

    private static readonly (string Code, ContentCategoryGroup Group)[] FirstEight =
    [
        ("ANIMALS", ContentCategoryGroup.Genre), ("FANTASY", ContentCategoryGroup.Genre), ("ADVENTURE", ContentCategoryGroup.Genre),
        ("SCIENCE", ContentCategoryGroup.Learning), ("FRIENDSHIP", ContentCategoryGroup.LifeValue), ("FAMILY", ContentCategoryGroup.Genre),
        ("VIOLENCE", ContentCategoryGroup.SafetyWarning), ("SCARY_CONTENT", ContentCategoryGroup.SafetyWarning)
    ];

    [PostgresFact]
    public async Task Migration_AddsTheMissingSeedLabels_AndKeepsExistingOrDeletedOnes()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var db = new ApplicationDbContext(database.Options);
        await MigrateToPreviousAsync(db);
        db.ContentCategories.AddRange(FirstEight.Select(item => new ContentCategory { Code = item.Code, DisplayName = item.Code, Group = item.Group }));
        db.ContentCategories.Add(new ContentCategory { Code = "HUMOR", DisplayName = "Đã xoá", IsDeleted = true });
        db.ContentCategories.Add(new ContentCategory { Code = "DINOSAURS", DisplayName = "Khủng long", IsActive = false });
        await db.SaveChangesAsync();

        await db.GetService<IMigrator>().MigrateAsync();

        var all = await db.ContentCategories.IgnoreQueryFilters().AsNoTracking().ToListAsync();
        Assert.Equal(8 + 2 + 31, all.Count);
        Assert.Equal(all.Count, all.Select(item => item.Code).Distinct().Count());
        Assert.True(all.Single(item => item.Code == "HUMOR").IsDeleted);
        Assert.False(all.Single(item => item.Code == "DINOSAURS").IsActive);
        Assert.Equal("ANIMALS", all.Single(item => item.Code == "ANIMALS").DisplayName);

        var profanity = all.Single(item => item.Code == "PROFANITY");
        Assert.Equal(("Ngôn ngữ thô tục", ContentCategoryGroup.SafetyWarning, true), (profanity.DisplayName, profanity.Group, profanity.IsActive));
        Assert.Equal(["Age_6_8", "Age_9_10"], profanity.DefaultBlockedAgeBands);
        Assert.Empty(all.Single(item => item.Code == "BULLYING").DefaultBlockedAgeBands);
        // Nhóm theo seed 03_content_categories.sql: 8 Học tập, 7 Cảnh báo an toàn (HUMOR thuộc Thể loại).
        Assert.Equal(8, all.Count(item => item.Group == ContentCategoryGroup.Learning));
        Assert.Equal(7, all.Count(item => item.Group == ContentCategoryGroup.SafetyWarning));

        // Id tự sinh không trùng nhau và nhãn mới vẫn thêm được sau migration.
        db.ContentCategories.Add(new ContentCategory { Code = "AFTER_IMPORT", DisplayName = "Sau khi nhập" });
        await db.SaveChangesAsync();
    }

    [PostgresFact]
    public async Task Migration_LeavesAnEmptyTableForTheSeed()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var db = new ApplicationDbContext(database.Options);
        await MigrateToPreviousAsync(db);
        Assert.False(await db.ContentCategories.IgnoreQueryFilters().AnyAsync());

        await db.GetService<IMigrator>().MigrateAsync();

        Assert.False(await db.ContentCategories.IgnoreQueryFilters().AnyAsync());
    }

    private static async Task MigrateToPreviousAsync(ApplicationDbContext db)
    {
        var migrations = db.Database.GetMigrations().ToList();
        await db.GetService<IMigrator>().MigrateAsync(migrations[migrations.FindIndex(id => id.EndsWith(Migration)) - 1]);
    }
}
