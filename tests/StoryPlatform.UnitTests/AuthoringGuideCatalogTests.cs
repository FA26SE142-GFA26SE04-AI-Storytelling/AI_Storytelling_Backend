using System.Text.Json;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ParentAuthoring;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed class AuthoringGuideCatalogTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    private static AuthoringGuide Question(string id = "q-1") =>
        new(id, "Parent", ["free"], "starting_point", "question", "Bạn muốn bắt đầu ở đâu?", "Đọc rồi bỏ qua.", null, null, [], 1);

    private static GuidePlaceholder[] P(params string[] keys) => keys.Select(k => new GuidePlaceholder(k, "Nhãn " + k, null)).ToArray();

    private static AuthoringGuide Template(string id = "t-1", string text = "Hôm ấy, {{person}} thử {{activity}}.",
        string[]? placeholders = null) =>
        new(id, "Parent", ["free"], "opening", "template", "Mẫu", "Điền rồi chèn.", null, text,
            P(placeholders ?? ["person", "activity"]), 2);

    private static void AssertInvalid(string code, params AuthoringGuide[] items) =>
        Assert.Equal(code, Assert.Throws<BadRequestException>(() => AuthoringGuideCatalogValidator.Validate(items)).Message);

    private static Task AddRowsAsync(ParentAuthoringTestStore store, int versionId, params AuthoringGuide[] guides) =>
        new AuthoringGuideItemStore(store.Uow.Object).AddAsync(versionId, guides, () => Task.CompletedTask, Ct);

    [Fact]
    public void Validator_accepts_valid_question_and_template() =>
        AuthoringGuideCatalogValidator.Validate([Question(), Template()]);

    [Fact]
    public void Validator_rejects_duplicate_ids() => AssertInvalid("GUIDE_ID_DUPLICATED", Question("x"), Template("x"));

    [Fact]
    public void Validator_rejects_missing_required_text() =>
        AssertInvalid("GUIDE_FIELD_REQUIRED", Question() with { Title = " " });

    [Fact]
    public void Validator_rejects_wrong_audience_purpose_group_and_type()
    {
        AssertInvalid("GUIDE_AUDIENCE_INVALID", Question() with { AudienceRole = "Child" });
        AssertInvalid("GUIDE_PURPOSE_INVALID", Question() with { Purposes = ["bedtime"] });
        AssertInvalid("GUIDE_PURPOSE_INVALID", Question() with { Purposes = [] });
        AssertInvalid("GUIDE_GROUP_INVALID", Question() with { SupportGroup = "intro" });
        AssertInvalid("GUIDE_ITEM_TYPE_INVALID", Question() with { ItemType = "quiz" });
    }

    [Fact]
    public void Validator_rejects_question_with_template_or_placeholders()
    {
        AssertInvalid("GUIDE_QUESTION_HAS_TEMPLATE", Question() with { TemplateText = "Xin chào" });
        AssertInvalid("GUIDE_QUESTION_HAS_TEMPLATE", Question() with { PlaceholderDefinitions = P("person") });
    }

    [Fact]
    public void Validator_requires_template_text_and_exact_placeholder_match()
    {
        AssertInvalid("GUIDE_TEMPLATE_REQUIRED", Template() with { TemplateText = null });
        AssertInvalid("GUIDE_PLACEHOLDERS_MISMATCH", Template(placeholders: ["person"]));
        AssertInvalid("GUIDE_PLACEHOLDERS_MISMATCH", Template(placeholders: ["person", "activity", "place"]));
    }

    [Fact]
    public async Task Seeder_loads_all_thirty_guides_as_published_parent_vi_1()
    {
        var store = new ParentAuthoringTestStore();
        var catalog = Assert.Single(store.Items<AuthoringGuideCatalog>());
        var version = Assert.Single(store.Items<AuthoringGuideCatalogVersion>());

        Assert.Equal(("vi", "Parent"), (catalog.Locale, catalog.AudienceRole));
        Assert.Equal(version.Id, catalog.PublishedVersionId);
        Assert.Null(catalog.DraftVersionId);
        Assert.Equal(("parent-vi-1", 1, AuthoringGuideVersionStatus.Published), (version.CatalogVersion, version.VersionNo, version.Status));

        Assert.Null(version.ContentJson);
        var source = AuthoringGuideCatalogSeeder.LoadEmbedded();
        var stored = await new AuthoringGuideItemStore(store.Uow.Object).LoadAsync(version.Id, "Parent", Ct);
        Assert.Equal(30, stored.Length);
        Assert.Equal(JsonSerializer.Serialize(source.Items, AuthoringGuideCatalogSeeder.Json),
            JsonSerializer.Serialize(stored, AuthoringGuideCatalogSeeder.Json));
        Assert.Equal(36, store.Items<AuthoringGuidePlaceholder>().Count);
    }

    [Fact]
    public async Task Seeder_runs_once_and_never_overwrites_edited_content()
    {
        var store = new ParentAuthoringTestStore();
        var row = store.Items<AuthoringGuideItem>().Single(x => x.GuideKey == "opening-1");
        row.Title = "Đã sửa tay";

        var seededAgain = await new AuthoringGuideCatalogSeeder(store.Uow.Object).SeedAsync(Ct);

        Assert.False(seededAgain);
        Assert.Single(store.Items<AuthoringGuideCatalog>());
        Assert.Single(store.Items<AuthoringGuideCatalogVersion>());
        Assert.Equal(30, store.Items<AuthoringGuideItem>().Count);
        Assert.Equal("Đã sửa tay", row.Title);
    }

    private static RenderGuideRequest Values(string? catalogVersion = null) =>
        new(new() { ["person"] = "An", ["words"] = "Chào" }, catalogVersion);

    [Fact]
    public async Task Parent_reads_published_catalog_with_same_counts_as_before()
    {
        var guides = new ParentAuthoringTestStore().Guides;
        var all = await guides.GetAsync("vi", "free", null, 1, 100, Ct);
        Assert.Equal(("parent-vi-1", 21), (all.CatalogVersion, all.TotalCount));
        Assert.Equal(Enumerable.Range(1, 30).Where(o => o is (>= 10 and <= 12) or >= 13).Order(),
            all.Items.Select(x => x.Order));
    }

    [Fact]
    public async Task Draft_content_is_never_served_to_parent()
    {
        var store = new ParentAuthoringTestStore();
        var catalog = store.Items<AuthoringGuideCatalog>().Single();
        store.Items<AuthoringGuideCatalogVersion>().Add(new()
        {
            Id = 99, CatalogId = catalog.Id, VersionNo = 2, CatalogVersion = "parent-vi-2", Status = AuthoringGuideVersionStatus.Draft,
        });
        await AddRowsAsync(store, 99, Question("draft-only"));
        catalog.DraftVersionId = 99;

        var page = await store.Guides.GetAsync("vi", "free", null, 1, 100, Ct);

        Assert.Equal("parent-vi-1", page.CatalogVersion);
        Assert.DoesNotContain(page.Items, x => x.Id == "draft-only");
        await Assert.ThrowsAsync<NotFoundException>(() => store.Guides.RenderAsync("draft-only", Values(), Ct));
    }

    [Fact]
    public async Task Render_checks_catalog_version_only_when_client_sends_it()
    {
        var guides = new ParentAuthoringTestStore().Guides;

        Assert.Equal("Chào", (await guides.RenderAsync("dialogue-1", Values(), Ct)).Text.Split('“', '”')[1]);
        Assert.Equal("parent-vi-1", (await guides.RenderAsync("dialogue-1", Values("parent-vi-1"), Ct)).CatalogVersion);
        var ex = await Assert.ThrowsAsync<ConflictException>(() => guides.RenderAsync("dialogue-1", Values("parent-vi-0"), Ct));
        Assert.Equal("GUIDE_CATALOG_CHANGED", ex.Message);
    }

    [Fact]
    public async Task Render_returns_not_found_when_guide_was_removed_from_published_set()
    {
        var store = new ParentAuthoringTestStore();
        store.Items<AuthoringGuideItem>().Single(x => x.GuideKey == "opening-1").IsDeleted = true;

        await Assert.ThrowsAsync<NotFoundException>(() => store.Guides.RenderAsync("opening-1", Values(), Ct));
    }

    [Fact]
    public async Task Question_cannot_be_rendered_and_missing_published_set_is_reported()
    {
        var store = new ParentAuthoringTestStore();
        var ex = await Assert.ThrowsAsync<BadRequestException>(() => store.Guides.RenderAsync("free-start-1", Values(), Ct));
        Assert.Equal("GUIDE_HAS_NO_TEMPLATE", ex.Message);

        store.Items<AuthoringGuideCatalog>().Single().PublishedVersionId = null;
        var unavailable = await Assert.ThrowsAsync<ConflictException>(() => store.Guides.GetAsync("vi", null, null, 1, 10, Ct));
        Assert.Equal("GUIDE_CATALOG_UNAVAILABLE", unavailable.Message);
    }

    [Fact]
    public void Validator_rejects_ids_that_are_not_url_safe()
    {
        AssertInvalid("GUIDE_ID_INVALID", Question("bad id"));
        AssertInvalid("GUIDE_ID_INVALID", Question("a/b"));
        AssertInvalid("GUIDE_ID_INVALID", Question(new string('a', 101)));
        AuthoringGuideCatalogValidator.Validate([Question(new string('a', 100)), Question("ok_id-1")]);
    }

    [Fact]
    public void Query_orders_by_order_then_id_filters_group_and_falls_back_to_free()
    {
        AuthoringGuide[] items = [Question("b") with { Order = 2 }, Question("a") with { Order = 2 }, Template("c") with { Order = 1 }];

        var all = AuthoringGuideCatalogQuery.Page("parent-vi-1", "vi", null, null, 1, 10, items);
        Assert.Equal(["c", "a", "b"], all.Items.Select(x => x.Id));
        Assert.Equal(("parent-vi-1", "vi", 3), (all.CatalogVersion, all.Locale, all.TotalCount));

        Assert.Equal(["c"], AuthoringGuideCatalogQuery.Page("v", "vi", null, "opening", 1, 10, items).Items.Select(x => x.Id));
        Assert.Equal(3, AuthoringGuideCatalogQuery.Page("v", "vi", "message", null, 1, 10, items).TotalCount);

        var second = AuthoringGuideCatalogQuery.Page("v", "vi", null, null, 2, 2, items);
        Assert.Equal(["b"], second.Items.Select(x => x.Id));
        Assert.Equal(3, second.TotalCount);
    }

    [Fact]
    public void Query_validates_paging_and_purpose()
    {
        Assert.Equal("PAGINATION_INVALID", Assert.Throws<BadRequestException>(() => AuthoringGuideCatalogQuery.ValidatePaging(null, 0, 10)).Message);
        Assert.Equal("PAGINATION_INVALID", Assert.Throws<BadRequestException>(() => AuthoringGuideCatalogQuery.ValidatePaging(null, 1, 101)).Message);
        AuthoringGuideCatalogQuery.ValidatePaging("family_memory", 1, 100);
        Assert.Throws<BadRequestException>(() => AuthoringGuideCatalogQuery.ValidatePaging("bedtime", 1, 10));
    }

    [Fact]
    public void Query_renders_plain_text_in_one_pass_and_rejects_bad_values()
    {
        var guide = Template();
        Assert.Equal("Hôm ấy, $1 thử {{x}}.", AuthoringGuideCatalogQuery.RenderText(guide with { TemplateText = "Hôm ấy, {{person}} thử {{activity}}.", PlaceholderDefinitions = P("person", "activity") },
            new Dictionary<string, string> { ["person"] = "$1", ["activity"] = "{{x}}" }));

        AssertRenderInvalid("GUIDE_HAS_NO_TEMPLATE", Question(), new Dictionary<string, string>());
        AssertRenderInvalid("PLACEHOLDER_VALUES_INVALID", guide, new Dictionary<string, string> { ["person"] = "An" });
        AssertRenderInvalid("PLACEHOLDER_VALUES_INVALID", guide, new Dictionary<string, string> { ["person"] = "An", ["activity"] = "x", ["extra"] = "y" });
        AssertRenderInvalid("PLACEHOLDER_VALUES_INVALID", guide, new Dictionary<string, string> { ["person"] = " ", ["activity"] = "x" });
        AssertRenderInvalid("PLACEHOLDER_VALUES_INVALID", guide, new Dictionary<string, string> { ["person"] = new string('a', 501), ["activity"] = "x" });
        AssertRenderInvalid("PLACEHOLDER_VALUES_INVALID", guide, null);
    }

    private static void AssertRenderInvalid(string code, AuthoringGuide guide, IReadOnlyDictionary<string, string>? values) =>
        Assert.Equal(code, Assert.Throws<BadRequestException>(() => AuthoringGuideCatalogQuery.RenderText(guide, values)).Message);
    [Fact]
    public async Task Parent_never_reads_a_version_that_is_not_published_even_if_the_pointer_targets_it()
    {
        var store = new ParentAuthoringTestStore();
        var catalog = store.Items<AuthoringGuideCatalog>().Single();
        store.Items<AuthoringGuideCatalogVersion>().Add(new()
        {
            Id = 88, CatalogId = catalog.Id, VersionNo = 2, CatalogVersion = "parent-vi-2", Status = AuthoringGuideVersionStatus.Draft,
        });
        await AddRowsAsync(store, 88, Question("draft-only"));
        catalog.PublishedVersionId = 88; // corrupted pointer: published pointer aimed at a Draft-status row

        var ex = await Assert.ThrowsAsync<ConflictException>(() => store.Guides.GetAsync("vi", "free", null, 1, 100, Ct));
        Assert.Equal("GUIDE_CATALOG_UNAVAILABLE", ex.Message);
    }

    [Fact]
    public void Validator_rejects_duplicate_or_empty_placeholder_keys()
    {
        AssertInvalid("GUIDE_PLACEHOLDERS_MISMATCH", Template(placeholders: ["person", "person", "activity"]));
        AssertInvalid("GUIDE_PLACEHOLDERS_MISMATCH", Template() with
        {
            PlaceholderDefinitions = [new("person", "Ai?", null), new("", "Làm gì?", null)]
        });
    }

    [Fact]
    public void Labels_are_optional_by_default_but_required_when_asked_and_always_bounded()
    {
        var unlabeled = Template() with { PlaceholderDefinitions = [new("person", null, null), new("activity", " ", null)] };
        AuthoringGuideCatalogValidator.ValidateItem(unlabeled); // dữ liệu cũ chưa có nhãn vẫn phát hành được
        Assert.Equal("GUIDE_PLACEHOLDER_LABEL_INVALID",
            Assert.Throws<BadRequestException>(() => AuthoringGuideCatalogValidator.ValidateItem(unlabeled, requireLabels: true)).Message);

        AssertInvalid("GUIDE_PLACEHOLDER_LABEL_INVALID", Template() with
        {
            PlaceholderDefinitions = [new("person", new string('a', 101), null), new("activity", "Làm gì?", null)]
        });
        AssertInvalid("GUIDE_PLACEHOLDER_LABEL_INVALID", Template() with
        {
            PlaceholderDefinitions = [new("person", "Ai?", new string('a', 201)), new("activity", "Làm gì?", null)]
        });
        AuthoringGuideCatalogValidator.ValidateItem(Template(), requireLabels: true);
    }

    [Fact]
    public void Seed_gives_every_blank_a_vietnamese_label_and_hint()
    {
        var seed = AuthoringGuideCatalogSeeder.LoadEmbedded();
        Assert.Equal(2, seed.SchemaVersion);
        var blanks = seed.Items.SelectMany(x => x.PlaceholderDefinitions).ToArray();
        Assert.Equal(36, blanks.Length);
        Assert.All(blanks, b => Assert.False(string.IsNullOrWhiteSpace(b.Label)));
        Assert.All(blanks, b => Assert.StartsWith("ví dụ: ", b.Hint));
        Assert.Equal(new GuidePlaceholder("person", "Ai?", "ví dụ: bố, bé Na"),
            seed.Items.Single(x => x.Id == "opening-1").PlaceholderDefinitions[0]);
    }
}
