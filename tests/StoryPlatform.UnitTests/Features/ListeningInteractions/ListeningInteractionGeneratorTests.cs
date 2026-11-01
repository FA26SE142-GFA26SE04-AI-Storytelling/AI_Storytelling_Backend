using Microsoft.EntityFrameworkCore;
using StoryPlatform.Application.Features.ListeningInteractions.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ListeningInteractions;

public sealed class ListeningInteractionGeneratorTests
{
    private static (StoryReviewServiceTests.FakeUnitOfWork Uow, ListeningInteractionGenerator Generator) Setup()
    {
        var uow = ListeningTestStory.SeedReviewStory();
        return (uow, new ListeningInteractionGenerator(uow, ListeningTestStory.AiClient().Object));
    }

    private static Task<StoryPlatform.Application.Features.ListeningInteractions.Models.ListeningScope> ScopeAsync(
        StoryReviewServiceTests.FakeUnitOfWork uow, ListeningInteractionGenerator generator) =>
        generator.LoadScopeAsync(uow.Items<Story>().Single(), uow.Items<StoryVersion>().Single(x => x.IsCurrent), default);

    [Fact]
    public async Task Generated_package_round_trips_through_the_rules()
    {
        var (uow, generator) = Setup();
        await ListeningTestStory.SeedPackageAsync(uow);

        var scope = await ScopeAsync(uow, generator);
        var inspection = await generator.InspectAsync(scope, default);

        Assert.True(inspection.Valid, string.Join(", ", inspection.Issues.Select(i => $"{i.Type}:{i.Code}@{i.Field}")));
        Assert.Equal(scope.Plan.Items.Sum(x => x.Count), inspection.Items.Count);
        Assert.All(inspection.Items, x => Assert.False(x.IsStale));
    }

    [Fact]
    public async Task Missing_type_is_reported()
    {
        var (uow, generator) = Setup();
        await ListeningTestStory.SeedPackageAsync(uow, ListeningInteractionType.Checkpoint);

        var inspection = await generator.InspectAsync(await ScopeAsync(uow, generator), default);

        Assert.Contains(inspection.Issues, i => i.Code == "LISTENING_MISSING" && i.Type == ListeningInteractionType.RolePlay);
        Assert.DoesNotContain(inspection.Issues, i => i.Type == ListeningInteractionType.Checkpoint);
    }

    [Fact]
    public async Task Content_shift_after_saving_is_reported_as_stale_anchor()
    {
        var (uow, generator) = Setup();
        await ListeningTestStory.SeedPackageAsync(uow);
        var version = uow.Items<StoryVersion>().Single();
        version.Content = "Ngày xưa, " + version.Content; // mọi offset lệch đi

        var inspection = await generator.InspectAsync(await ScopeAsync(uow, generator), default);

        Assert.Contains(inspection.Issues, i => i.Code == "STALE_ANCHOR");
        Assert.Contains(inspection.Items, i => i.IsStale);
    }

    [Fact]
    public async Task Replace_soft_deletes_old_rows_retires_media_and_keeps_package_valid()
    {
        var (uow, generator) = Setup();
        await ListeningTestStory.SeedPackageAsync(uow);
        var old = uow.Items<ListeningInteraction>().Where(x => x.Type == ListeningInteractionType.Checkpoint).ToArray();
        var oldIds = old.Select(x => x.Id).ToArray();
        var media = new MediaAsset { Id = 1, ListeningInteractionId = old[0].Id, IsCurrent = true };
        uow.Seed(media);
        var scope = await ScopeAsync(uow, generator);

        var drafts = (await generator.GenerateAsync(scope, ListeningInteractionType.Checkpoint, "regen", default)).Drafts;
        await generator.ReplaceAsync(scope.Version.Id, ListeningInteractionType.Checkpoint, drafts, default);

        Assert.All(old, x => Assert.True(x.IsDeleted));
        Assert.All(uow.Items<ListeningInteractionOption>().Where(o => oldIds.Contains(o.ListeningInteractionId)), o => Assert.True(o.IsDeleted));
        Assert.False(media.IsCurrent);
        Assert.Equal(2, uow.Items<ListeningInteraction>().Count(x => x.Type == ListeningInteractionType.Checkpoint && !x.IsDeleted));
        Assert.True((await generator.InspectAsync(scope, default)).Valid);
    }

    [Fact]
    public async Task Type_outside_the_plan_cannot_be_generated()
    {
        var (uow, generator) = Setup(); // 6-8 không có FillBlank

        var error = await Assert.ThrowsAsync<StoryPlatform.Application.Common.Exceptions.ConflictException>(async () =>
            await generator.GenerateAsync(await ScopeAsync(uow, generator), ListeningInteractionType.FillBlank, "x", default));

        Assert.Equal("LISTENING_TYPE_NOT_IN_PLAN", error.Message);
    }

    [Fact]
    public async Task Missing_generation_context_fails_closed()
    {
        var uow = ListeningTestStory.SeedReviewStory(withContext: false);
        var generator = new ListeningInteractionGenerator(uow, ListeningTestStory.AiClient().Object);

        var error = await Assert.ThrowsAsync<StoryPlatform.Application.Common.Exceptions.ConflictException>(() => ScopeAsync(uow, generator));

        Assert.Equal("LISTENING_CONTEXT_MISSING", error.Message);
    }

    /// <summary>Repository giả compile biểu thức nên không bắt được lỗi dịch truy vấn; test này chạy EF thật trên SQLite.</summary>
    [Fact]
    public async Task Generate_inspect_and_replace_translate_on_real_ef()
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new CreatorManualMediaPersistenceTests.Db(
            new DbContextOptionsBuilder<StoryPlatform.Infrastructure.Persistence.ApplicationDbContext>()
                .UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var version = new StoryVersion { Id = 1, StoryId = 1, VersionNo = 1, IsCurrent = true, Title = "Thỏ con",
            Content = ListeningTestStory.Content, Lesson = "Nhờ người lớn giúp khi bị lạc" };
        db.Add(new Story { Id = 1, Title = "Thỏ con", AuthorUserId = 1 });
        db.Add(version);
        await db.SaveChangesAsync();
        var uow = new StoryPlatform.Infrastructure.Persistence.Repositories.UnitOfWork(db);
        var generator = new ListeningInteractionGenerator(uow, ListeningTestStory.AiClient().Object);
        var scope = ListeningInteractionGenerator.CreateScope(version,
            new StoryPlatform.Application.Features.AIStoryInput.Models.AIStoryInputContextSnapshot(
                1, "6-8", 2, "level_2", "vi", 1000, "always_manual", [], [], [], []));

        foreach (var item in scope.Plan.Items)
            await generator.InsertAsync(version.Id, (await generator.GenerateAsync(scope, item.Type, "sqlite", default)).Drafts, default);
        await uow.SaveChangesAsync();
        var drafts = (await generator.GenerateAsync(scope, ListeningInteractionType.Checkpoint, "sqlite-regen", default)).Drafts;
        await generator.ReplaceAsync(version.Id, ListeningInteractionType.Checkpoint, drafts, default);
        var inspection = await generator.InspectAsync(scope, default);

        Assert.True(inspection.Valid, string.Join(", ", inspection.Issues.Select(i => $"{i.Type}:{i.Code}@{i.Field}")));
        Assert.Equal(scope.Plan.Items.Sum(x => x.Count), inspection.Items.Count);
        Assert.Equal(2, db.Set<ListeningInteraction>().IgnoreQueryFilters().Count(x => x.Type == ListeningInteractionType.Checkpoint && x.IsDeleted));
    }
}
