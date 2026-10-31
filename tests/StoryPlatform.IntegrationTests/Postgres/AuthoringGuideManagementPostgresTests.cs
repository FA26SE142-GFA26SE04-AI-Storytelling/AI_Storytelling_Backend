using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AuthoringGuideManagement.DTOs;
using StoryPlatform.Application.Features.ParentAuthoring;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.Infrastructure.Migrations;
using Xunit;

namespace StoryPlatform.IntegrationTests.Postgres;

public sealed class AuthoringGuideManagementPostgresTests(AuthoringGuidePostgresFixture fixture) : IClassFixture<AuthoringGuidePostgresFixture>
{
    private static UpsertGuideItemRequest Item(string? token, string title = "Mẫu mới") =>
        new(token, ["free"], "opening", "template", title, "Điền rồi chèn.", null, "Sáng nay, {{person}} thử {{activity}}.",
            [new GuidePlaceholder("person", "Ai?", null), new GuidePlaceholder("activity", "Làm gì?", null)], 31);

    private static async Task<(T? Result, Exception? Error)> TryAsync<T>(Func<Task<T>> action)
    {
        try { return (await action(), null); }
        catch (Exception ex) { return (default, ex); }
    }

    private static void AssertLostRace(Exception? error, params string[] acceptedCodes)
    {
        var conflict = Assert.IsType<ConflictException>(error); // nếu là DbUpdateException/lỗi khác, test đỏ: đó là lỗi 500 thật
        Assert.Contains(conflict.Message, acceptedCodes);
    }

    [PostgresFact]
    public async Task Seeded_database_serves_parent_vi_1_to_parents_and_to_both_staff_roles()
    {
        await using var db = await fixture.CreateDatabaseAsync();
        await using var scope = db.NewScope();

        var parent = await scope.Parent.GetAsync("vi", "free", null, 1, 100);
        Assert.Equal(("parent-vi-1", 21), (parent.CatalogVersion, parent.TotalCount));
        foreach (var staff in new[] { db.ModeratorId, db.AdministratorId })
        {
            var overview = await scope.Management.GetOverviewAsync(staff);
            Assert.Equal(30, overview.Published!.Items.Length);
            Assert.Null(overview.Draft);
        }
        await Assert.ThrowsAsync<ForbiddenException>(() => scope.Management.GetOverviewAsync(db.ParentUserId));
    }

    [PostgresFact]
    public async Task Full_cycle_draft_edit_preview_publish_restore_discard_matches_the_spec_on_a_real_database()
    {
        await using var db = await fixture.CreateDatabaseAsync();
        GuideDraftChangeDto draft;
        string token;
        string originalContent;

        await using (var s = db.NewScope())
        {
            originalContent = JsonSerializer.Serialize((await s.Management.GetOverviewAsync(db.ModeratorId)).Published!.Items);
            draft = await s.Management.CreateDraftAsync(db.ModeratorId, new(null));
            Assert.Equal(("parent-vi-2", 30), (draft.Draft.CatalogVersion, draft.Draft.ItemCount));
            token = (await s.Management.UpsertItemAsync(db.ModeratorId, "opening-9", Item(draft.RevisionToken))).RevisionToken;
        }
        await using (var s = db.NewScope())
        {
            var preview = await s.Management.PreviewAsync(db.AdministratorId,
                new(token, null, null, GuideId: "opening-9", Values: new() { ["person"] = "An", ["activity"] = "đạp xe" }));
            Assert.Equal("Sáng nay, An thử đạp xe.", preview.Render!.Text);
            Assert.Equal(token, preview.RevisionToken); // xem trước không đổi token
            var parentBefore = await s.Parent.GetAsync("vi", "free", null, 1, 100);
            Assert.Equal(("parent-vi-1", 21), (parentBefore.CatalogVersion, parentBefore.TotalCount)); // Parent chưa thấy nháp
        }
        await using (var s = db.NewScope())
        {
            var published = await s.Management.PublishAsync(db.AdministratorId, new(token));
            Assert.Equal(("Published", "parent-vi-2", 31), (published.Published.Status, published.Published.CatalogVersion, published.Published.ItemCount));
            var parent = await s.Parent.GetAsync("vi", "free", null, 1, 100);
            Assert.Equal(("parent-vi-2", 22), (parent.CatalogVersion, parent.TotalCount));
            var rendered = await s.Parent.RenderAsync("opening-9",
                new(new() { ["person"] = "An", ["activity"] = "đạp xe" }, "parent-vi-2"));
            Assert.Equal("Sáng nay, An thử đạp xe.", rendered.Text);
            Assert.True(rendered.IsPlainText);
            var stale = await Assert.ThrowsAsync<ConflictException>(() =>
                s.Parent.RenderAsync("dialogue-1", new(new() { ["person"] = "An", ["words"] = "Chào" }, "parent-vi-1")));
            Assert.Equal("GUIDE_CATALOG_CHANGED", stale.Message);
            token = published.RevisionToken;
        }
        await using (var s = db.NewScope()) // khôi phục phiên bản cũ thành phiên bản mới
        {
            var v1 = (await s.Management.ListVersionsAsync(db.ModeratorId, 1, 10)).Items.Single(x => x.CatalogVersion == "parent-vi-1").VersionId;
            var restore = await s.Management.CreateDraftAsync(db.ModeratorId, new(v1));
            Assert.Equal("parent-vi-3", restore.Draft.CatalogVersion);
            var restored = await s.Management.PublishAsync(db.ModeratorId, new(restore.RevisionToken));
            var parent = await s.Parent.GetAsync("vi", "free", null, 1, 100);
            Assert.Equal(("parent-vi-3", 21), (parent.CatalogVersion, parent.TotalCount));
            token = restored.RevisionToken;
        }
        await using (var s = db.NewScope()) // hủy bản nháp (xóa mềm) rồi dùng lại số phiên bản
        {
            var d4 = await s.Management.CreateDraftAsync(db.ModeratorId, new(null));
            Assert.Equal("parent-vi-4", d4.Draft.CatalogVersion);
            var discarded = await s.Management.DiscardDraftAsync(db.ModeratorId, d4.RevisionToken);
            await Assert.ThrowsAsync<NotFoundException>(() => s.Management.GetVersionAsync(db.ModeratorId, discarded.DiscardedVersionId));
            Assert.Equal(3, (await s.Management.ListVersionsAsync(db.ModeratorId, 1, 10)).TotalCount);

            var again = await s.Management.CreateDraftAsync(db.ModeratorId, new(null));
            Assert.Equal("parent-vi-4", again.Draft.CatalogVersion);
            await s.Management.RemoveItemAsync(db.ModeratorId, "opening-1", again.RevisionToken);

            var raw = await s.Context.AuthoringGuideCatalogVersions.IgnoreQueryFilters().Where(x => x.VersionNo == 4).ToListAsync();
            Assert.Equal(2, raw.Count);
            Assert.Single(raw, x => x.IsDeleted);
        }
        await using (var s = db.NewScope()) // mỗi thao tác ghi có đúng một dòng audit
        {
            var original = await s.Context.AuthoringGuideCatalogVersions.SingleAsync(x => x.VersionNo == 1);
            Assert.Equal(originalContent, JsonSerializer.Serialize((await s.Management.GetVersionAsync(db.ModeratorId, original.Id)).Items));
            Assert.Equal(AuthoringGuideVersionStatus.Published, original.Status);
            var counts = await s.Context.AuditLogs.Where(x => x.EntityType == nameof(AuthoringGuideCatalogVersion))
                .GroupBy(x => x.Action).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count);
            Assert.Equal(4, counts["AuthoringGuideDraftCreated"]);
            Assert.Equal(1, counts["AuthoringGuideItemUpserted"]);
            Assert.Equal(1, counts["AuthoringGuideItemRemoved"]);
            Assert.Equal(1, counts["AuthoringGuideDraftDiscarded"]);
            Assert.Equal(2, counts["AuthoringGuideCatalogPublished"]);
        }
    }

    [PostgresFact]
    public async Task Two_moderators_creating_a_draft_at_the_same_time_one_wins_the_other_gets_409_never_a_raw_database_error()
    {
        await using var db = await fixture.CreateDatabaseAsync();
        for (var round = 0; round < 5; round++)
        {
            await using var a = db.NewScope();
            await using var b = db.NewScope();
            var results = await Task.WhenAll(
                TryAsync(() => a.Management.CreateDraftAsync(db.ModeratorId, new(null))),
                TryAsync(() => b.Management.CreateDraftAsync(db.AdministratorId, new(null))));

            Assert.Single(results, r => r.Error is null);
            AssertLostRace(Assert.Single(results, r => r.Error is not null).Error, "GUIDE_REVISION_CONFLICT", "GUIDE_DRAFT_EXISTS");

            await using var c = db.NewScope(); // dọn để vòng sau lại tranh nhau
            var overview = await c.Management.GetOverviewAsync(db.ModeratorId);
            Assert.NotNull(overview.Draft);
            await c.Management.DiscardDraftAsync(db.ModeratorId, overview.RevisionToken);
        }
    }

    [PostgresFact]
    public async Task Two_editors_with_the_same_token_only_one_edit_lands()
    {
        await using var db = await fixture.CreateDatabaseAsync();
        string token;
        await using (var s = db.NewScope()) token = (await s.Management.CreateDraftAsync(db.ModeratorId, new(null))).RevisionToken;

        await using var a = db.NewScope();
        await using var b = db.NewScope();
        var results = await Task.WhenAll(
            TryAsync(() => a.Management.UpsertItemAsync(db.ModeratorId, "race-a", Item(token))),
            TryAsync(() => b.Management.UpsertItemAsync(db.AdministratorId, "race-b", Item(token))));

        Assert.Single(results, r => r.Error is null);
        AssertLostRace(Assert.Single(results, r => r.Error is not null).Error, "GUIDE_REVISION_CONFLICT");

        await using var c = db.NewScope();
        var items = (await c.Management.GetOverviewAsync(db.ModeratorId)).Draft!.Items;
        Assert.Equal(1, items.Count(x => x.Id.StartsWith("race-")));
        Assert.Equal(31, items.Length);
    }

    [PostgresFact]
    public async Task Two_publishers_with_the_same_token_publish_exactly_one_new_version()
    {
        await using var db = await fixture.CreateDatabaseAsync();
        string token;
        await using (var s = db.NewScope())
        {
            var d = await s.Management.CreateDraftAsync(db.ModeratorId, new(null));
            token = (await s.Management.UpsertItemAsync(db.ModeratorId, "opening-9", Item(d.RevisionToken))).RevisionToken;
        }

        await using var a = db.NewScope();
        await using var b = db.NewScope();
        var results = await Task.WhenAll(
            TryAsync(() => a.Management.PublishAsync(db.ModeratorId, new(token))),
            TryAsync(() => b.Management.PublishAsync(db.AdministratorId, new(token))));

        Assert.Single(results, r => r.Error is null);
        AssertLostRace(Assert.Single(results, r => r.Error is not null).Error, "GUIDE_REVISION_CONFLICT", "GUIDE_DRAFT_NOT_FOUND");

        await using var c = db.NewScope();
        Assert.Equal(2, (await c.Management.ListVersionsAsync(db.ModeratorId, 1, 10)).TotalCount);
        Assert.Equal(1, await c.Context.AuditLogs.CountAsync(x => x.Action == "AuthoringGuideCatalogPublished"));
        Assert.Equal("parent-vi-2", (await c.Parent.GetAsync("vi", "free", null, 1, 100)).CatalogVersion);
    }

    [PostgresFact]
    public async Task Database_constraints_reject_a_second_live_draft_and_a_duplicate_live_version_number()
    {
        await using var db = await fixture.CreateDatabaseAsync();
        int catalogId;
        await using (var s = db.NewScope())
        {
            await s.Management.CreateDraftAsync(db.ModeratorId, new(null));
            catalogId = (await s.Context.AuthoringGuideCatalogs.SingleAsync()).Id;
        }

        await using (var s = db.NewScope()) // hai bản Draft chưa xóa trong cùng một bộ
        {
            s.Context.AuthoringGuideCatalogVersions.Add(new() { CatalogId = catalogId, VersionNo = 99, CatalogVersion = "x-99",
                Status = AuthoringGuideVersionStatus.Draft });
            var ex = await Assert.ThrowsAsync<DbUpdateException>(() => s.Context.SaveChangesAsync());
            Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(ex.InnerException).SqlState);
        }
        await using (var s = db.NewScope()) // trùng (CatalogId, VersionNo) khi cả hai còn sống
        {
            s.Context.AuthoringGuideCatalogVersions.Add(new() { CatalogId = catalogId, VersionNo = 1, CatalogVersion = "x-1",
                Status = AuthoringGuideVersionStatus.Published });
            var ex = await Assert.ThrowsAsync<DbUpdateException>(() => s.Context.SaveChangesAsync());
            Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(ex.InnerException).SqlState);
        }
    }

    [PostgresFact]
    public async Task Parent_never_reads_a_version_that_is_not_published_even_if_the_pointer_targets_it()
    {
        await using var db = await fixture.CreateDatabaseAsync();
        await using var scope = db.NewScope();
        var draft = await scope.Management.CreateDraftAsync(db.ModeratorId, new(null));

        var catalog = await scope.Context.AuthoringGuideCatalogs.SingleAsync();
        catalog.PublishedVersionId = draft.Draft.VersionId;
        await scope.Context.SaveChangesAsync();

        await using var parentScope = db.NewScope();
        var ex = await Assert.ThrowsAsync<ConflictException>(() => parentScope.Parent.GetAsync("vi", "free", null, 1, 100));
        Assert.Equal("GUIDE_CATALOG_UNAVAILABLE", ex.Message);
    }

    [PostgresFact]
    public async Task A_failed_publish_rolls_back_completely_on_a_real_database()
    {
        await using var db = await fixture.CreateDatabaseAsync();
        string token;
        int draftId;
        await using (var s = db.NewScope())
        {
            var d = await s.Management.CreateDraftAsync(db.ModeratorId, new(null));
            draftId = d.Draft.VersionId;
            token = d.RevisionToken;
            await s.Context.AuthoringGuideItems.Where(x => x.CatalogVersionId == draftId)
                .ExecuteUpdateAsync(x => x.SetProperty(i => i.IsDeleted, true)); // bộ rỗng → không được phát hành
        }

        await using (var s = db.NewScope())
        {
            var ex = await Assert.ThrowsAsync<BadRequestException>(() => s.Management.PublishAsync(db.ModeratorId, new(token)));
            Assert.Equal("GUIDE_CATALOG_EMPTY", ex.Message);
        }
        await using (var s = db.NewScope())
        {
            var catalog = await s.Context.AuthoringGuideCatalogs.SingleAsync();
            Assert.Equal((draftId, token), (catalog.DraftVersionId, catalog.RevisionToken));
            Assert.Equal(AuthoringGuideVersionStatus.Draft, (await s.Context.AuthoringGuideCatalogVersions.SingleAsync(x => x.Id == draftId)).Status);
            Assert.Equal("parent-vi-1", (await s.Parent.GetAsync("vi", "free", null, 1, 100)).CatalogVersion);
            Assert.Equal(0, await s.Context.AuditLogs.CountAsync(x => x.Action == "AuthoringGuideCatalogPublished"));
        }

        // Bộ rỗng chỉ kiểm tra validation. Ép PostgreSQL từ chối ghi audit sau khi
        // trạng thái publish đã SaveChanges để kiểm tra rollback transaction thật.
        await using (var s = db.NewScope())
        {
            await s.Context.AuthoringGuideItems.IgnoreQueryFilters().Where(x => x.CatalogVersionId == draftId)
                .ExecuteUpdateAsync(x => x.SetProperty(i => i.IsDeleted, false));
            await s.Context.Database.ExecuteSqlRawAsync("""
                ALTER TABLE audit_logs ADD CONSTRAINT it_reject_guide_publish_audit
                CHECK ("Action" <> 'AuthoringGuideCatalogPublished')
                """);
        }
        await using (var s = db.NewScope())
        {
            var ex = await Assert.ThrowsAsync<DbUpdateException>(() => s.Management.PublishAsync(db.ModeratorId, new(token)));
            var pg = Assert.IsType<PostgresException>(ex.InnerException);
            Assert.Equal(PostgresErrorCodes.CheckViolation, pg.SqlState);
            Assert.Equal("it_reject_guide_publish_audit", pg.ConstraintName);
        }
        await using (var s = db.NewScope())
        {
            var catalog = await s.Context.AuthoringGuideCatalogs.SingleAsync();
            Assert.Equal((draftId, token), (catalog.DraftVersionId, catalog.RevisionToken));
            var draft = await s.Context.AuthoringGuideCatalogVersions.SingleAsync(x => x.Id == draftId);
            Assert.Equal(AuthoringGuideVersionStatus.Draft, draft.Status);
            Assert.Null(draft.PublishedAt);
            Assert.Null(draft.PublishedByUserId);
            Assert.Equal("parent-vi-1", (await s.Parent.GetAsync("vi", "free", null, 1, 100)).CatalogVersion);
            Assert.Equal(0, await s.Context.AuditLogs.CountAsync(x => x.Action == "AuthoringGuideCatalogPublished"));
        }
    }

    private const string LegacyContent = """
        {"schemaVersion":1,"items":[
          {"id":"legacy-q","audienceRole":"Parent","purposes":["free"],"supportGroup":"starting_point","itemType":"question",
           "title":"Hỏi cũ","instruction":"Đọc","example":null,"templateText":null,"placeholderDefinitions":[],"order":2},
          {"id":"legacy-t","audienceRole":"Parent","purposes":["free","message"],"supportGroup":"opening","itemType":"template",
           "title":"Mẫu cũ","instruction":"Điền","example":"Ví dụ","templateText":"{{person}} ở {{place}}",
           "placeholderDefinitions":["person","place"],"order":1}]}
        """;

    [PostgresFact]
    public async Task Backfill_turns_legacy_json_into_rows_once_and_keeps_blank_order()
    {
        await using var db = await fixture.CreateDatabaseAsync();
        int versionId;
        await using (var s = db.NewScope())
        {
            var catalogId = (await s.Context.AuthoringGuideCatalogs.SingleAsync()).Id;
            var legacy = new AuthoringGuideCatalogVersion { CatalogId = catalogId, VersionNo = 50, CatalogVersion = "legacy-50",
                Status = AuthoringGuideVersionStatus.Published, ContentJson = LegacyContent };
            s.Context.AuthoringGuideCatalogVersions.Add(legacy);
            await s.Context.SaveChangesAsync();
            versionId = legacy.Id;

            await s.Context.Database.ExecuteSqlRawAsync(NormalizeAuthoringGuideItems.BackfillSql);
            await s.Context.Database.ExecuteSqlRawAsync(NormalizeAuthoringGuideItems.BackfillSql); // chạy lại không nhân đôi
        }
        await using (var s = db.NewScope())
        {
            var items = (await s.Management.GetVersionAsync(db.ModeratorId, versionId)).Items;
            Assert.Equal(["legacy-t", "legacy-q"], items.Select(x => x.Id));
            var template = items[0];
            Assert.Equal((new[] { "free", "message" }, "Ví dụ", "{{person}} ở {{place}}"),
                (template.Purposes, template.Example, template.TemplateText));
            Assert.Equal([new GuidePlaceholder("person", null, null), new GuidePlaceholder("place", null, null)], template.PlaceholderDefinitions);
            Assert.Empty(items[1].PlaceholderDefinitions);
            Assert.Equal(2, await s.Context.AuthoringGuideItems.CountAsync(x => x.CatalogVersionId == versionId));
        }
    }

    [PostgresFact]
    public async Task Rollback_refuses_while_labels_exist_and_otherwise_rebuilds_legacy_json()
    {
        await using var db = await fixture.CreateDatabaseAsync();
        await using var s = db.NewScope();

        var refused = await Assert.ThrowsAsync<PostgresException>(() =>
            s.Context.Database.ExecuteSqlRawAsync(NormalizeAuthoringGuideItems.RollbackGuardSql)); // seed đã có nhãn
        Assert.Contains("rollback would lose placeholder labels", refused.MessageText);

        await s.Context.Database.ExecuteSqlRawAsync(NormalizeAuthoringGuideItems.RollbackSql);
        var json = (await s.Context.AuthoringGuideCatalogVersions.AsNoTracking().SingleAsync()).ContentJson!;
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(1, doc.RootElement.GetProperty("schemaVersion").GetInt32());
        var items = doc.RootElement.GetProperty("items");
        Assert.Equal(30, items.GetArrayLength());
        var opening = items.EnumerateArray().Single(x => x.GetProperty("id").GetString() == "opening-1");
        Assert.Equal(["person", "companion", "activity"],
            opening.GetProperty("placeholderDefinitions").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal("Parent", opening.GetProperty("audienceRole").GetString());
    }
}
