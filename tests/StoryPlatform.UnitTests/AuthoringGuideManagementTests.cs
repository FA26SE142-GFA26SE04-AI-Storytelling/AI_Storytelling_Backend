using Moq;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.AuthoringGuideManagement.DTOs;
using StoryPlatform.Application.Features.AuthoringGuideManagement.Services;
using StoryPlatform.Application.Features.ParentAuthoring;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed class AuthoringGuideManagementTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private const int Moderator = 14;
    private const int Admin = 9;
    private const int ParentUser = 1;

    private sealed class Harness
    {
        public ParentAuthoringTestStore Store { get; } = new();
        public Mock<IAuditLogWriter> Audit { get; } = new();
        public AuthoringGuideManagementService Service { get; }

        public Harness()
        {
            Store.Items<UserAccount>().Add(new() { Id = Moderator, Role = UserRole.Moderator, Status = AccountStatus.Active });
            Store.Items<UserAccount>().Add(new() { Id = 15, Role = UserRole.Moderator, Status = AccountStatus.Suspended });
            Service = new AuthoringGuideManagementService(Store.Uow.Object, Audit.Object);
        }

        public AuthoringGuideCatalog Catalog => Store.Items<AuthoringGuideCatalog>().Single();
    }

    private static UpsertGuideItemRequest Item(string? token, string text = "Sáng nay, {{person}} thử {{activity}}.",
        string[]? placeholders = null, string title = "Mẫu mới") =>
        new(token, ["free"], "opening", "template", title, "Điền rồi chèn.", null, text, placeholders ?? ["person", "activity"], 31);

    [Fact]
    public async Task Only_active_moderator_or_administrator_may_manage()
    {
        var h = new Harness();
        await Assert.ThrowsAsync<ForbiddenException>(() => h.Service.GetOverviewAsync(ParentUser, Ct));
        await Assert.ThrowsAsync<ForbiddenException>(() => h.Service.GetOverviewAsync(15, Ct));
        await Assert.ThrowsAsync<ForbiddenException>(() => h.Service.GetOverviewAsync(999, Ct));
        Assert.NotNull(await h.Service.GetOverviewAsync(Moderator, Ct));
        Assert.NotNull(await h.Service.GetOverviewAsync(Admin, Ct));
    }

    [Fact]
    public async Task Overview_shows_published_set_no_draft_and_the_revision_token()
    {
        var h = new Harness();
        var overview = await h.Service.GetOverviewAsync(Moderator, Ct);

        Assert.Equal(("vi", "Parent", h.Catalog.RevisionToken), (overview.Locale, overview.AudienceRole, overview.RevisionToken));
        Assert.Null(overview.Draft);
        Assert.Equal(30, overview.Published!.Items.Length);
        Assert.Equal(("parent-vi-1", 1, "Published", 30),
            (overview.Published.Version.CatalogVersion, overview.Published.Version.VersionNo, overview.Published.Version.Status, overview.Published.Version.ItemCount));
    }

    [Fact]
    public async Task Version_history_is_paged_newest_first_and_validates_paging()
    {
        var h = new Harness();
        var page = await h.Service.ListVersionsAsync(Moderator, 1, 10, Ct);
        Assert.Equal(1, page.TotalCount);
        Assert.Equal("parent-vi-1", Assert.Single(page.Items).CatalogVersion);

        Assert.Equal("PAGINATION_INVALID", (await Assert.ThrowsAsync<BadRequestException>(() => h.Service.ListVersionsAsync(Moderator, 0, 10, Ct))).Message);
        Assert.Equal("PAGINATION_INVALID", (await Assert.ThrowsAsync<BadRequestException>(() => h.Service.ListVersionsAsync(Moderator, 1, 101, Ct))).Message);
    }

    [Fact]
    public async Task Version_detail_returns_content_and_hides_versions_of_other_catalogs()
    {
        var h = new Harness();
        var versionId = h.Store.Items<AuthoringGuideCatalogVersion>().Single().Id;
        var detail = await h.Service.GetVersionAsync(Moderator, versionId, Ct);
        Assert.Equal(30, detail.Items.Length);
        Assert.Equal(1, detail.SchemaVersion);

        h.Store.Items<AuthoringGuideCatalogVersion>().Add(new() { Id = 77, CatalogId = 999, VersionNo = 1, CatalogVersion = "other-1", ContentJson = "{}" });
        await Assert.ThrowsAsync<NotFoundException>(() => h.Service.GetVersionAsync(Moderator, 77, Ct));
        await Assert.ThrowsAsync<NotFoundException>(() => h.Service.GetVersionAsync(Moderator, 12345, Ct));
    }

    [Fact]
    public async Task Create_draft_copies_the_published_set_rotates_the_token_and_audits()
    {
        var h = new Harness();
        var oldToken = h.Catalog.RevisionToken;

        var created = await h.Service.CreateDraftAsync(Moderator, new(null), Ct);

        Assert.Equal(("Draft", 2, "parent-vi-2", 30), (created.Draft.Status, created.Draft.VersionNo, created.Draft.CatalogVersion, created.Draft.ItemCount));
        Assert.NotEqual(oldToken, created.RevisionToken);
        Assert.Equal(created.RevisionToken, h.Catalog.RevisionToken);
        Assert.Equal(created.Draft.VersionId, h.Catalog.DraftVersionId);
        Assert.Equal(Moderator, h.Store.Items<AuthoringGuideCatalogVersion>().Single(x => x.Id == created.Draft.VersionId).CreatedByUserId);
        h.Audit.Verify(x => x.LogAsync(Moderator, "AuthoringGuideDraftCreated", nameof(AuthoringGuideCatalogVersion),
            created.Draft.VersionId, null, It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
        // Parent vẫn nhận bộ đã phát hành.
        Assert.Equal("parent-vi-1", (await h.Store.Guides.GetAsync("vi", "free", null, 1, 100, Ct)).CatalogVersion);
    }

    [Fact]
    public async Task Only_one_draft_may_exist_and_a_source_version_must_be_a_published_version_of_this_set()
    {
        var h = new Harness();
        var publishedId = h.Catalog.PublishedVersionId!.Value;
        await h.Service.CreateDraftAsync(Moderator, new(publishedId), Ct);

        Assert.Equal("GUIDE_DRAFT_EXISTS", (await Assert.ThrowsAsync<ConflictException>(() => h.Service.CreateDraftAsync(Admin, new(null), Ct))).Message);

        var fresh = new Harness();
        await Assert.ThrowsAsync<NotFoundException>(() => fresh.Service.CreateDraftAsync(Moderator, new(424242), Ct));
        fresh.Store.Items<AuthoringGuideCatalogVersion>().Add(new()
        {
            Id = 50, CatalogId = fresh.Catalog.Id, VersionNo = 9, CatalogVersion = "x", Status = AuthoringGuideVersionStatus.Draft, ContentJson = "{}"
        });
        Assert.Equal("GUIDE_SOURCE_VERSION_INVALID",
            (await Assert.ThrowsAsync<BadRequestException>(() => fresh.Service.CreateDraftAsync(Moderator, new(50), Ct))).Message);
    }

    [Fact]
    public async Task Upsert_adds_then_updates_in_place_without_touching_the_published_set()
    {
        var h = new Harness();
        var t0 = (await h.Service.CreateDraftAsync(Moderator, new(null), Ct)).RevisionToken;

        var added = await h.Service.UpsertItemAsync(Moderator, "opening-9", Item(t0), Ct);
        Assert.Equal(31, added.Draft.ItemCount);
        Assert.NotEqual(t0, added.RevisionToken);

        var updated = await h.Service.UpsertItemAsync(Admin, "opening-1", Item(added.RevisionToken, title: "Tiêu đề đã sửa"), Ct);
        Assert.Equal(31, updated.Draft.ItemCount);
        var draftItems = (await h.Service.GetVersionAsync(Moderator, updated.Draft.VersionId, Ct)).Items;
        Assert.Equal("Tiêu đề đã sửa", draftItems.Single(x => x.Id == "opening-1").Title);
        Assert.Equal("Parent", draftItems.Single(x => x.Id == "opening-9").AudienceRole);

        var published = await h.Service.GetVersionAsync(Moderator, h.Catalog.PublishedVersionId!.Value, Ct);
        Assert.Equal(30, published.Items.Length);
        Assert.Equal("Mẫu diễn đạt 1", published.Items.Single(x => x.Id == "opening-1").Title);
        Assert.Equal(21, (await h.Store.Guides.GetAsync("vi", "free", null, 1, 100, Ct)).TotalCount);
        h.Audit.Verify(x => x.LogAsync(It.IsAny<int?>(), "AuthoringGuideItemUpserted", nameof(AuthoringGuideCatalogVersion),
            It.IsAny<int>(), It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Two_people_with_the_same_token_only_one_succeeds()
    {
        var h = new Harness();
        var t0 = (await h.Service.CreateDraftAsync(Moderator, new(null), Ct)).RevisionToken;
        await h.Service.UpsertItemAsync(Moderator, "opening-9", Item(t0), Ct);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => h.Service.UpsertItemAsync(Admin, "opening-8", Item(t0), Ct));
        Assert.Equal("GUIDE_REVISION_CONFLICT", ex.Message);
        var draft = await h.Service.GetVersionAsync(Moderator, h.Catalog.DraftVersionId!.Value, Ct);
        Assert.DoesNotContain(draft.Items, x => x.Id == "opening-8");

        Assert.Equal("GUIDE_REVISION_TOKEN_REQUIRED",
            (await Assert.ThrowsAsync<BadRequestException>(() => h.Service.UpsertItemAsync(Moderator, "opening-8", Item(null), Ct))).Message);
        Assert.Equal("GUIDE_REVISION_TOKEN_REQUIRED",
            (await Assert.ThrowsAsync<BadRequestException>(() => h.Service.RemoveItemAsync(Moderator, "opening-1", " ", Ct))).Message);
    }

    [Fact]
    public async Task Invalid_items_are_rejected_and_nothing_changes()
    {
        var h = new Harness();
        var t0 = (await h.Service.CreateDraftAsync(Moderator, new(null), Ct)).RevisionToken;
        h.Audit.Invocations.Clear();

        var mismatch = await Assert.ThrowsAsync<BadRequestException>(() =>
            h.Service.UpsertItemAsync(Moderator, "bad-1", Item(t0, "Xin chào {{person}}", ["person", "place"]), Ct));
        Assert.Equal("GUIDE_PLACEHOLDERS_MISMATCH", mismatch.Message);
        var badId = await Assert.ThrowsAsync<BadRequestException>(() => h.Service.UpsertItemAsync(Moderator, "bad id", Item(t0), Ct));
        Assert.Equal("GUIDE_ID_INVALID", badId.Message);

        Assert.Equal(t0, h.Catalog.RevisionToken);
        Assert.Empty(h.Audit.Invocations);
        Assert.Equal(30, (await h.Service.GetVersionAsync(Moderator, h.Catalog.DraftVersionId!.Value, Ct)).Items.Length);
    }

    [Fact]
    public async Task Editing_without_a_draft_is_404_and_remove_checks_the_item_exists()
    {
        var h = new Harness();
        var token = h.Catalog.RevisionToken;
        Assert.Equal("GUIDE_DRAFT_NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(() => h.Service.UpsertItemAsync(Moderator, "x", Item(token), Ct))).Message);
        Assert.Equal("GUIDE_DRAFT_NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(() => h.Service.RemoveItemAsync(Moderator, "opening-1", token, Ct))).Message);

        var t0 = (await h.Service.CreateDraftAsync(Moderator, new(null), Ct)).RevisionToken;
        Assert.Equal("GUIDE_ITEM_NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(() => h.Service.RemoveItemAsync(Moderator, "nope", t0, Ct))).Message);

        var removed = await h.Service.RemoveItemAsync(Moderator, "opening-1", t0, Ct);
        Assert.Equal(29, removed.Draft.ItemCount);
        Assert.NotEqual(t0, removed.RevisionToken);
        var draft = await h.Service.GetVersionAsync(Moderator, removed.Draft.VersionId, Ct);
        Assert.DoesNotContain(draft.Items, x => x.Id == "opening-1");
        Assert.Equal(21, (await h.Store.Guides.GetAsync("vi", "free", null, 1, 100, Ct)).TotalCount);
        h.Audit.Verify(x => x.LogAsync(Moderator, "AuthoringGuideItemRemoved", nameof(AuthoringGuideCatalogVersion),
            removed.Draft.VersionId, It.IsAny<object?>(), null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_failing_audit_rolls_the_whole_draft_creation_back()
    {
        var h = new Harness();
        var token = h.Catalog.RevisionToken;
        h.Audit.Setup(x => x.LogAsync(It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("audit down"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Service.CreateDraftAsync(Moderator, new(null), Ct));

        Assert.Single(h.Store.Items<AuthoringGuideCatalogVersion>());
        Assert.Null(h.Catalog.DraftVersionId);
        Assert.Equal(token, h.Catalog.RevisionToken);
    }

    private static async Task<(Harness H, string Token)> DraftWithNewItemAsync()
    {
        var h = new Harness();
        var t0 = (await h.Service.CreateDraftAsync(Moderator, new(null), Ct)).RevisionToken;
        var t1 = (await h.Service.UpsertItemAsync(Moderator, "opening-9", Item(t0), Ct)).RevisionToken;
        return (h, t1);
    }

    [Fact]
    public async Task Preview_lists_the_draft_the_way_parent_would_see_it_without_changing_anything()
    {
        var (h, token) = await DraftWithNewItemAsync();
        h.Audit.Invocations.Clear();

        var preview = await h.Service.PreviewAsync(Moderator, new(token, "free", null, 1, 100), Ct);

        Assert.Null(preview.Render);
        Assert.Equal(("parent-vi-2", 22), (preview.Page!.CatalogVersion, preview.Page.TotalCount));
        Assert.Contains(preview.Page.Items, x => x.Id == "opening-9");
        Assert.Equal(token, preview.RevisionToken);
        Assert.Equal(token, h.Catalog.RevisionToken);
        Assert.Empty(h.Audit.Invocations);
        Assert.Equal(21, (await h.Store.Guides.GetAsync("vi", "free", null, 1, 100, Ct)).TotalCount);
    }

    [Fact]
    public async Task Preview_renders_a_draft_template_and_applies_the_same_rules_as_parent()
    {
        var (h, token) = await DraftWithNewItemAsync();

        var render = (await h.Service.PreviewAsync(Moderator, new(token, null, null, GuideId: "opening-9",
            Values: new() { ["person"] = "<b>An</b>", ["activity"] = "{{person}}" }), Ct)).Render!;
        Assert.Equal(("parent-vi-2", "opening-9", "Sáng nay, <b>An</b> thử {{person}}.", true),
            (render.CatalogVersion, render.GuideId, render.Text, render.IsPlainText));

        Assert.Equal("PLACEHOLDER_VALUES_INVALID", (await Assert.ThrowsAsync<BadRequestException>(() =>
            h.Service.PreviewAsync(Moderator, new(token, null, null, GuideId: "opening-9", Values: new() { ["person"] = "An" }), Ct))).Message);
        Assert.Equal("GUIDE_HAS_NO_TEMPLATE", (await Assert.ThrowsAsync<BadRequestException>(() =>
            h.Service.PreviewAsync(Moderator, new(token, null, null, GuideId: "free-start-1"), Ct))).Message);
        await Assert.ThrowsAsync<NotFoundException>(() => h.Service.PreviewAsync(Moderator, new(token, null, null, GuideId: "missing"), Ct));
        Assert.Equal("PAGINATION_INVALID", (await Assert.ThrowsAsync<BadRequestException>(() =>
            h.Service.PreviewAsync(Moderator, new(token, null, null, 1, 0), Ct))).Message);
    }

    [Fact]
    public async Task Preview_requires_a_current_token_a_draft_and_valid_content()
    {
        var h = new Harness();
        Assert.Equal("GUIDE_DRAFT_NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(() =>
            h.Service.PreviewAsync(Moderator, new(h.Catalog.RevisionToken, null, null), Ct))).Message);

        var t0 = (await h.Service.CreateDraftAsync(Moderator, new(null), Ct)).RevisionToken;
        Assert.Equal("GUIDE_REVISION_CONFLICT", (await Assert.ThrowsAsync<ConflictException>(() =>
            h.Service.PreviewAsync(Moderator, new("stale", null, null), Ct))).Message);
        Assert.Equal("GUIDE_REVISION_TOKEN_REQUIRED", (await Assert.ThrowsAsync<BadRequestException>(() =>
            h.Service.PreviewAsync(Moderator, new(null, null, null), Ct))).Message);

        h.Store.Items<AuthoringGuideCatalogVersion>().Single(x => x.Status == AuthoringGuideVersionStatus.Draft).ContentJson =
            "{\"schemaVersion\":1,\"items\":[{\"id\":\"x\",\"audienceRole\":\"Parent\",\"purposes\":[\"free\"],\"supportGroup\":\"opening\"," +
            "\"itemType\":\"template\",\"title\":\"t\",\"instruction\":\"i\",\"example\":null,\"templateText\":\"{{a}}\",\"placeholderDefinitions\":[],\"order\":1}]}";
        Assert.Equal("GUIDE_PLACEHOLDERS_MISMATCH", (await Assert.ThrowsAsync<BadRequestException>(() =>
            h.Service.PreviewAsync(Moderator, new(t0, null, null), Ct))).Message);
    }

    [Fact]
    public async Task Publish_swaps_the_whole_set_atomically_freezes_history_and_audits()
    {
        var (h, token) = await DraftWithNewItemAsync();
        var oldPublishedId = h.Catalog.PublishedVersionId!.Value;
        var draftId = h.Catalog.DraftVersionId!.Value;

        var published = await h.Service.PublishAsync(Admin, new(token), Ct);

        Assert.Equal(("Published", "parent-vi-2", 31), (published.Published.Status, published.Published.CatalogVersion, published.Published.ItemCount));
        Assert.Equal(Admin, published.Published.PublishedByUserId);
        Assert.NotNull(published.Published.PublishedAt);
        Assert.NotEqual(token, published.RevisionToken);
        Assert.Equal(((int?)draftId, (int?)null, published.RevisionToken), (h.Catalog.PublishedVersionId, h.Catalog.DraftVersionId, h.Catalog.RevisionToken));

        var parent = await h.Store.Guides.GetAsync("vi", "free", null, 1, 100, Ct);
        Assert.Equal(("parent-vi-2", 22), (parent.CatalogVersion, parent.TotalCount));
        var old = h.Store.Items<AuthoringGuideCatalogVersion>().Single(x => x.Id == oldPublishedId);
        Assert.Equal((AuthoringGuideVersionStatus.Published, "parent-vi-1"), (old.Status, old.CatalogVersion));
        Assert.Equal(30, (await h.Service.GetVersionAsync(Moderator, oldPublishedId, Ct)).Items.Length);
        h.Audit.Verify(x => x.LogAsync(Admin, "AuthoringGuideCatalogPublished", nameof(AuthoringGuideCatalogVersion),
            draftId, It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);

        // Bản đã phát hành không sửa được: không còn bản nháp để sửa.
        var contentBefore = h.Store.Items<AuthoringGuideCatalogVersion>().Single(x => x.Id == draftId).ContentJson;
        Assert.Equal("GUIDE_DRAFT_NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(() =>
            h.Service.UpsertItemAsync(Moderator, "opening-1", Item(published.RevisionToken, title: "Sửa lén"), Ct))).Message);
        Assert.Equal(contentBefore, h.Store.Items<AuthoringGuideCatalogVersion>().Single(x => x.Id == draftId).ContentJson);
    }

    [Fact]
    public async Task Publish_needs_the_current_token_a_draft_and_a_non_empty_valid_set()
    {
        var h = new Harness();
        Assert.Equal("GUIDE_DRAFT_NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(() =>
            h.Service.PublishAsync(Moderator, new(h.Catalog.RevisionToken), Ct))).Message);

        var t0 = (await h.Service.CreateDraftAsync(Moderator, new(null), Ct)).RevisionToken;
        Assert.Equal("GUIDE_REVISION_CONFLICT", (await Assert.ThrowsAsync<ConflictException>(() =>
            h.Service.PublishAsync(Moderator, new("stale"), Ct))).Message);
        Assert.Equal("GUIDE_REVISION_TOKEN_REQUIRED", (await Assert.ThrowsAsync<BadRequestException>(() =>
            h.Service.PublishAsync(Moderator, new(null), Ct))).Message);

        var draft = h.Store.Items<AuthoringGuideCatalogVersion>().Single(x => x.Status == AuthoringGuideVersionStatus.Draft);
        draft.ContentJson = "{\"schemaVersion\":1,\"items\":[]}";
        Assert.Equal("GUIDE_CATALOG_EMPTY", (await Assert.ThrowsAsync<BadRequestException>(() =>
            h.Service.PublishAsync(Moderator, new(t0), Ct))).Message);

        // Không rỗng nhưng không hợp lệ: vẫn bị chặn và không thay đổi gì.
        var draftId = h.Catalog.DraftVersionId;
        draft = h.Store.Items<AuthoringGuideCatalogVersion>().Single(x => x.Id == draftId);
        draft.ContentJson =
            "{\"schemaVersion\":1,\"items\":[{\"id\":\"x\",\"audienceRole\":\"Parent\",\"purposes\":[\"free\"],\"supportGroup\":\"opening\"," +
            "\"itemType\":\"template\",\"title\":\"t\",\"instruction\":\"i\",\"example\":null,\"templateText\":\"{{a}}\",\"placeholderDefinitions\":[],\"order\":1}]}";
        Assert.Equal("GUIDE_PLACEHOLDERS_MISMATCH", (await Assert.ThrowsAsync<BadRequestException>(() =>
            h.Service.PublishAsync(Moderator, new(t0), Ct))).Message);
        Assert.Equal(AuthoringGuideVersionStatus.Draft, h.Store.Items<AuthoringGuideCatalogVersion>().Single(x => x.Id == draftId).Status);
        Assert.Equal(draftId, h.Catalog.DraftVersionId);

        // Không có gì thay đổi sau các lần thất bại.
        Assert.Equal(1, h.Catalog.PublishedVersionId);
        Assert.NotNull(h.Catalog.DraftVersionId);
        Assert.Equal(t0, h.Catalog.RevisionToken);
        Assert.Equal("parent-vi-1", (await h.Store.Guides.GetAsync("vi", "free", null, 1, 100, Ct)).CatalogVersion);
    }

    [Fact]
    public async Task Restore_copies_an_old_version_into_a_new_draft_and_publishes_it_as_a_new_version()
    {
        var (h, token) = await DraftWithNewItemAsync();
        var v1Id = h.Catalog.PublishedVersionId!.Value;
        await h.Service.PublishAsync(Moderator, new(token), Ct);

        var restoring = await h.Service.CreateDraftAsync(Moderator, new(v1Id), Ct);
        Assert.Equal(("parent-vi-3", 30), (restoring.Draft.CatalogVersion, restoring.Draft.ItemCount));
        var restored = await h.Service.PublishAsync(Moderator, new(restoring.RevisionToken), Ct);

        var parent = await h.Store.Guides.GetAsync("vi", "free", null, 1, 100, Ct);
        Assert.Equal(("parent-vi-3", 21), (parent.CatalogVersion, parent.TotalCount));
        Assert.Equal(3, (await h.Service.ListVersionsAsync(Moderator, 1, 10, Ct)).TotalCount);
        Assert.Equal(["parent-vi-3", "parent-vi-2", "parent-vi-1"],
            (await h.Service.ListVersionsAsync(Moderator, 1, 10, Ct)).Items.Select(x => x.CatalogVersion));
        Assert.Equal("Published", restored.Published.Status);
    }

    [Fact]
    public async Task A_failing_audit_during_publish_leaves_parent_on_the_old_set()
    {
        var (h, token) = await DraftWithNewItemAsync();
        var publishedBefore = h.Catalog.PublishedVersionId;
        var draftBefore = h.Catalog.DraftVersionId;
        h.Audit.Setup(x => x.LogAsync(It.IsAny<int?>(), "AuthoringGuideCatalogPublished", It.IsAny<string>(), It.IsAny<int>(),
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("audit down"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Service.PublishAsync(Moderator, new(token), Ct));

        Assert.Equal((publishedBefore, draftBefore, token), (h.Catalog.PublishedVersionId, h.Catalog.DraftVersionId, h.Catalog.RevisionToken));
        Assert.Equal(AuthoringGuideVersionStatus.Draft, h.Store.Items<AuthoringGuideCatalogVersion>().Single(x => x.Id == draftBefore).Status);
        Assert.Equal("parent-vi-1", (await h.Store.Guides.GetAsync("vi", "free", null, 1, 100, Ct)).CatalogVersion);
    }

    [Fact]
    public async Task Create_draft_checks_the_catalog_token_at_the_database_before_inserting_the_draft_row()
    {
        var h = new Harness();
        var oldToken = h.Catalog.RevisionToken;
        int? versionCountAtFirstSave = null;
        h.Store.Uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => versionCountAtFirstSave ??= h.Store.Items<AuthoringGuideCatalogVersion>().Count)
            .ReturnsAsync(1);

        var created = await h.Service.CreateDraftAsync(Moderator, new(null), Ct);

        Assert.Equal(1, versionCountAtFirstSave);
        Assert.NotEqual(oldToken, created.RevisionToken);
        Assert.Equal(created.RevisionToken, h.Catalog.RevisionToken);
        Assert.Equal(created.Draft.VersionId, h.Catalog.DraftVersionId);
    }

    [Fact]
    public async Task A_conflict_detected_by_the_database_surfaces_as_guide_revision_conflict()
    {
        // CreateDraft
        var h = new Harness();
        var published = h.Catalog.PublishedVersionId;
        var token = h.Catalog.RevisionToken;
        h.Store.Uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new ConflictException("db conflict"));
        Assert.Equal("GUIDE_REVISION_CONFLICT", (await Assert.ThrowsAsync<ConflictException>(() =>
            h.Service.CreateDraftAsync(Moderator, new(null), Ct))).Message);
        Assert.Equal((published, (int?)null, token), (h.Catalog.PublishedVersionId, h.Catalog.DraftVersionId, h.Catalog.RevisionToken));
        Assert.Single(h.Store.Items<AuthoringGuideCatalogVersion>());

        // Upsert
        var (h2, t1) = await DraftWithNewItemAsync();
        var draftId = h2.Catalog.DraftVersionId;
        h2.Store.Uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new ConflictException("db conflict"));
        Assert.Equal("GUIDE_REVISION_CONFLICT", (await Assert.ThrowsAsync<ConflictException>(() =>
            h2.Service.UpsertItemAsync(Moderator, "opening-9", Item(t1, title: "Khác"), Ct))).Message);
        Assert.Equal(t1, h2.Catalog.RevisionToken);

        // Publish
        var publishedBefore = h2.Catalog.PublishedVersionId;
        Assert.Equal("GUIDE_REVISION_CONFLICT", (await Assert.ThrowsAsync<ConflictException>(() =>
            h2.Service.PublishAsync(Moderator, new(t1), Ct))).Message);
        Assert.Equal((publishedBefore, draftId, t1), (h2.Catalog.PublishedVersionId, h2.Catalog.DraftVersionId, h2.Catalog.RevisionToken));
        Assert.Equal(AuthoringGuideVersionStatus.Draft, h2.Store.Items<AuthoringGuideCatalogVersion>().Single(x => x.Id == draftId).Status);
    }

    [Fact]
    public async Task Discard_draft_soft_deletes_it_hides_it_everywhere_and_keeps_the_published_set()
    {
        var (h, token) = await DraftWithNewItemAsync();
        var draftId = h.Catalog.DraftVersionId!.Value;
        var publishedId = h.Catalog.PublishedVersionId!.Value;
        h.Audit.Invocations.Clear();

        var discarded = await h.Service.DiscardDraftAsync(Moderator, token, Ct);

        Assert.Equal((draftId, "parent-vi-2"), (discarded.DiscardedVersionId, discarded.DiscardedCatalogVersion));
        Assert.NotEqual(token, discarded.RevisionToken);
        Assert.Equal(((int?)publishedId, (int?)null, discarded.RevisionToken),
            (h.Catalog.PublishedVersionId, h.Catalog.DraftVersionId, h.Catalog.RevisionToken));
        var row = h.Store.Items<AuthoringGuideCatalogVersion>().Single(x => x.Id == draftId); // soft delete: the row is kept
        Assert.True(row.IsDeleted);
        Assert.Equal(Moderator, row.UpdatedByUserId);
        h.Audit.Verify(x => x.LogAsync(Moderator, "AuthoringGuideDraftDiscarded", nameof(AuthoringGuideCatalogVersion), draftId,
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);

        Assert.Null((await h.Service.GetOverviewAsync(Moderator, Ct)).Draft);
        Assert.Equal(1, (await h.Service.ListVersionsAsync(Moderator, 1, 10, Ct)).TotalCount);
        await Assert.ThrowsAsync<NotFoundException>(() => h.Service.GetVersionAsync(Moderator, draftId, Ct));
        var parent = await h.Store.Guides.GetAsync("vi", "free", null, 1, 100, Ct);
        Assert.Equal(("parent-vi-1", 21), (parent.CatalogVersion, parent.TotalCount));
    }

    [Fact]
    public async Task Discard_draft_needs_staff_a_current_token_and_an_open_draft()
    {
        var h = new Harness();
        Assert.Equal("GUIDE_DRAFT_NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(() =>
            h.Service.DiscardDraftAsync(Moderator, h.Catalog.RevisionToken, Ct))).Message);

        var t0 = (await h.Service.CreateDraftAsync(Moderator, new(null), Ct)).RevisionToken;
        await Assert.ThrowsAsync<ForbiddenException>(() => h.Service.DiscardDraftAsync(ParentUser, t0, Ct));
        Assert.Equal("GUIDE_REVISION_TOKEN_REQUIRED", (await Assert.ThrowsAsync<BadRequestException>(() =>
            h.Service.DiscardDraftAsync(Moderator, " ", Ct))).Message);
        Assert.Equal("GUIDE_REVISION_CONFLICT", (await Assert.ThrowsAsync<ConflictException>(() =>
            h.Service.DiscardDraftAsync(Moderator, "stale", Ct))).Message);

        Assert.False(h.Store.Items<AuthoringGuideCatalogVersion>().Single(x => x.Status == AuthoringGuideVersionStatus.Draft).IsDeleted);
        Assert.Equal(t0, h.Catalog.RevisionToken);
        Assert.NotNull(h.Catalog.DraftVersionId);
    }

    [Fact]
    public async Task A_published_version_can_never_be_discarded()
    {
        var (h, token) = await DraftWithNewItemAsync();
        var published = await h.Service.PublishAsync(Moderator, new(token), Ct);
        List<(int, bool, string)> Snapshot() =>
            h.Store.Items<AuthoringGuideCatalogVersion>().Select(x => (x.Id, x.IsDeleted, x.ContentJson)).ToList();
        var before = Snapshot();

        Assert.Equal("GUIDE_DRAFT_NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(() =>
            h.Service.DiscardDraftAsync(Moderator, published.RevisionToken, Ct))).Message);

        Assert.Equal(before, Snapshot());
        Assert.Equal(published.Published.VersionId, h.Catalog.PublishedVersionId);
    }

    [Fact]
    public async Task After_a_discard_a_new_draft_reuses_the_number_and_starts_from_the_published_set()
    {
        var (h, token) = await DraftWithNewItemAsync(); // discarded draft = v2 with 31 items
        await h.Service.DiscardDraftAsync(Moderator, token, Ct);

        var created = await h.Service.CreateDraftAsync(Moderator, new(null), Ct);

        Assert.Equal(("parent-vi-2", 2, 30), (created.Draft.CatalogVersion, created.Draft.VersionNo, created.Draft.ItemCount));
        Assert.Equal(2, h.Store.Items<AuthoringGuideCatalogVersion>().Count(x => x.VersionNo == 2)); // one soft-deleted, one live
        Assert.DoesNotContain((await h.Service.GetVersionAsync(Moderator, created.Draft.VersionId, Ct)).Items, x => x.Id == "opening-9");

        await h.Service.PublishAsync(Moderator, new(created.RevisionToken), Ct);
        var parent = await h.Store.Guides.GetAsync("vi", "free", null, 1, 100, Ct);
        Assert.Equal(("parent-vi-2", 21), (parent.CatalogVersion, parent.TotalCount));
        Assert.Equal(2, (await h.Service.ListVersionsAsync(Moderator, 1, 10, Ct)).TotalCount);
    }

    [Fact]
    public async Task A_failing_audit_or_a_database_conflict_leaves_the_draft_untouched_on_discard()
    {
        var (h, token) = await DraftWithNewItemAsync();
        var draftId = h.Catalog.DraftVersionId;
        h.Audit.Setup(x => x.LogAsync(It.IsAny<int?>(), "AuthoringGuideDraftDiscarded", It.IsAny<string>(), It.IsAny<int>(),
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("audit down"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Service.DiscardDraftAsync(Moderator, token, Ct));
        Assert.Equal((draftId, token), (h.Catalog.DraftVersionId, h.Catalog.RevisionToken));
        Assert.False(h.Store.Items<AuthoringGuideCatalogVersion>().Single(x => x.Id == draftId).IsDeleted);

        h.Store.Uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new ConflictException("db conflict"));
        Assert.Equal("GUIDE_REVISION_CONFLICT", (await Assert.ThrowsAsync<ConflictException>(() =>
            h.Service.DiscardDraftAsync(Moderator, token, Ct))).Message);
        Assert.Equal((draftId, token), (h.Catalog.DraftVersionId, h.Catalog.RevisionToken));
        Assert.False(h.Store.Items<AuthoringGuideCatalogVersion>().Single(x => x.Id == draftId).IsDeleted);
    }
}
