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

    private static AuthoringGuide Template(string id = "t-1", string text = "Hôm ấy, {{person}} thử {{activity}}.",
        string[]? placeholders = null) =>
        new(id, "Parent", ["free"], "opening", "template", "Mẫu", "Điền rồi chèn.", null, text,
            placeholders ?? ["person", "activity"], 2);

    private static void AssertInvalid(string code, params AuthoringGuide[] items) =>
        Assert.Equal(code, Assert.Throws<BadRequestException>(() => AuthoringGuideCatalogValidator.Validate(items)).Message);

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
        AssertInvalid("GUIDE_QUESTION_HAS_TEMPLATE", Question() with { PlaceholderDefinitions = ["person"] });
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

        var source = AuthoringGuideCatalogSeeder.LoadEmbedded();
        var stored = JsonSerializer.Deserialize<AuthoringGuideCatalogContent>(version.ContentJson, AuthoringGuideCatalogSeeder.Json)!;
        Assert.Equal(30, stored.Items.Length);
        Assert.Equal(1, stored.SchemaVersion);
        Assert.Equal(JsonSerializer.Serialize(source.Items, AuthoringGuideCatalogSeeder.Json),
            JsonSerializer.Serialize(stored.Items, AuthoringGuideCatalogSeeder.Json));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Seeder_runs_once_and_never_overwrites_edited_content()
    {
        var store = new ParentAuthoringTestStore();
        var version = store.Items<AuthoringGuideCatalogVersion>().Single();
        version.ContentJson = "{\"schemaVersion\":1,\"items\":[]}";

        var seededAgain = await new AuthoringGuideCatalogSeeder(store.Uow.Object).SeedAsync(Ct);

        Assert.False(seededAgain);
        Assert.Single(store.Items<AuthoringGuideCatalog>());
        Assert.Single(store.Items<AuthoringGuideCatalogVersion>());
        Assert.Equal("{\"schemaVersion\":1,\"items\":[]}", version.ContentJson);
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
            ContentJson = JsonSerializer.Serialize(new AuthoringGuideCatalogContent(1, [Question("draft-only")]), AuthoringGuideCatalogSeeder.Json)
        });
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
        var version = store.Items<AuthoringGuideCatalogVersion>().Single();
        var content = JsonSerializer.Deserialize<AuthoringGuideCatalogContent>(version.ContentJson, AuthoringGuideCatalogSeeder.Json)!;
        version.ContentJson = JsonSerializer.Serialize(
            content with { Items = content.Items.Where(x => x.Id != "opening-1").ToArray() }, AuthoringGuideCatalogSeeder.Json);

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
}
