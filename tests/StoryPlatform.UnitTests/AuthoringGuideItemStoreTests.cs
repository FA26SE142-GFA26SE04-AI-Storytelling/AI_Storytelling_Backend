using StoryPlatform.Application.Features.ParentAuthoring;
using StoryPlatform.Domain.Entities;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed class AuthoringGuideItemStoreTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private const int Version = 500;

    private static AuthoringGuide Template(string id, int order, params (string Key, string? Label)[] blanks) =>
        new(id, "Parent", ["free", "message"], "opening", "template", "Mẫu " + id, "Điền rồi chèn.", "Ví dụ " + id,
            string.Join(" ", blanks.Select(b => "{{" + b.Key + "}}")),
            blanks.Select(b => new GuidePlaceholder(b.Key, b.Label, b.Label is null ? null : "ví dụ: " + b.Key)).ToArray(), order);

    private static AuthoringGuide Question(string id, int order) =>
        new(id, "Parent", ["free"], "starting_point", "question", "Câu hỏi " + id, "Đọc rồi bỏ qua.", null, null, [], order);

    private static (ParentAuthoringTestStore Store, AuthoringGuideItemStore Items, Func<Task> Save) Create()
    {
        var store = new ParentAuthoringTestStore();
        return (store, new AuthoringGuideItemStore(store.Uow.Object), () => Task.CompletedTask);
    }

    // Fake store đã có dữ liệu seed (từ Task 4 seed ghi vào cùng hai bảng), nên mọi kiểm tra chỉ xét dòng của phiên bản thử nghiệm, kể cả dòng đã xóa mềm.
    private static List<AuthoringGuideItem> Rows(ParentAuthoringTestStore store) =>
        store.Items<AuthoringGuideItem>().Where(x => x.CatalogVersionId == Version).ToList();

    private static List<AuthoringGuidePlaceholder> Blanks(ParentAuthoringTestStore store)
    {
        var ids = Rows(store).Select(x => x.Id).ToHashSet();
        return store.Items<AuthoringGuidePlaceholder>().Where(x => ids.Contains(x.ItemId)).ToList();
    }

    private static string Json(IEnumerable<AuthoringGuide> guides) =>
        System.Text.Json.JsonSerializer.Serialize(guides, AuthoringGuideCatalogSeeder.Json);

    [Fact]
    public async Task Added_guides_load_back_identically_in_order_then_key_order()
    {
        var (store, items, save) = Create();
        AuthoringGuide[] guides = [Template("b", 2, ("person", "Ai?"), ("place", null)), Question("a", 2), Question("c", 1)];

        await items.AddAsync(Version, guides, save, Ct);

        Assert.Equal(Json([guides[2], guides[1], guides[0]]), Json(await items.LoadAsync(Version, "Parent", Ct)));
        Assert.Equal(3, await items.CountAsync(Version, Ct));
        var blanks = Blanks(store);
        Assert.Equal([("person", 1), ("place", 2)], blanks.Select(x => (x.Key, x.Order)));
        Assert.All(blanks, x => Assert.Equal(Rows(store).Single(i => i.GuideKey == "b").Id, x.ItemId));
        Assert.Empty(await items.LoadAsync(Version + 1, "Parent", Ct));
    }

    [Fact]
    public async Task Upsert_updates_the_row_in_place_and_replaces_its_blanks()
    {
        var (store, items, save) = Create();
        await items.AddAsync(Version, [Template("t", 1, ("person", "Ai?"), ("activity", "Làm gì?"))], save, Ct);
        var rowId = Rows(store).Single().Id;
        var changed = Template("t", 7, ("place", "Ở đâu?")) with { Title = "Đã sửa" };

        var before = await items.UpsertAsync(Version, "Parent", changed, save, Ct);

        Assert.Equal("Mẫu t", before!.Title);
        Assert.Equal(["person", "activity"], before.PlaceholderDefinitions.Select(x => x.Key));
        Assert.Equal(Json([changed]), Json(await items.LoadAsync(Version, "Parent", Ct)));
        Assert.Equal(rowId, Rows(store).Single().Id);
        Assert.Equal([("person", true), ("activity", true), ("place", false)],
            Blanks(store).Select(x => (x.Key, x.IsDeleted)));
    }

    [Fact]
    public async Task Upsert_of_an_unknown_key_adds_it_and_returns_null()
    {
        var (_, items, save) = Create();
        Assert.Null(await items.UpsertAsync(Version, "Parent", Question("new-1", 3), save, Ct));
        Assert.Equal(["new-1"], (await items.LoadAsync(Version, "Parent", Ct)).Select(x => x.Id));
    }

    [Fact]
    public async Task Remove_soft_deletes_the_row_and_its_blanks_and_frees_the_key()
    {
        var (store, items, save) = Create();
        await items.AddAsync(Version, [Template("t", 1, ("person", "Ai?")), Question("q", 2)], save, Ct);

        var removed = await items.RemoveAsync(Version, "Parent", "t", Ct);

        Assert.Equal("t", removed!.Id);
        Assert.Equal(["q"], (await items.LoadAsync(Version, "Parent", Ct)).Select(x => x.Id));
        Assert.Equal(1, await items.CountAsync(Version, Ct));
        Assert.True(Rows(store).Single(x => x.GuideKey == "t").IsDeleted);
        Assert.All(Blanks(store), x => Assert.True(x.IsDeleted));
        Assert.Null(await items.RemoveAsync(Version, "Parent", "t", Ct));

        await items.UpsertAsync(Version, "Parent", Template("t", 1, ("person", "Ai?")), save, Ct);
        Assert.Equal(2, Rows(store).Count(x => x.GuideKey == "t"));
    }

    [Fact]
    public async Task Add_calls_save_before_writing_blanks_so_they_reference_real_item_ids()
    {
        var (store, items, _) = Create();
        var placeholderCountAtSave = new List<int>();
        Task Save() { placeholderCountAtSave.Add(Blanks(store).Count); return Task.CompletedTask; }

        await items.AddAsync(Version, [Template("t", 1, ("person", "Ai?"))], Save, Ct);

        Assert.Equal([0, 1], placeholderCountAtSave);
    }
}
