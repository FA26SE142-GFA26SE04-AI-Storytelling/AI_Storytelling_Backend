using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Application.Features.ParentAuthoring;

/// <summary>Đọc/ghi mục hướng dẫn và chỗ trống của một phiên bản; dùng chung cho nạp dữ liệu, Parent và Moderator.</summary>
public sealed class AuthoringGuideItemStore(IUnitOfWork uow)
{
    public async Task<AuthoringGuide[]> LoadAsync(int versionId, string audienceRole, CancellationToken ct)
    {
        var rows = await uow.Repository<AuthoringGuideItem>().FindAsync(
            x => x.CatalogVersionId == versionId && !x.IsDeleted, cancellationToken: ct);
        if (rows.Count == 0) return [];
        var ids = rows.Select(x => x.Id).ToArray();
        var blanks = (await uow.Repository<AuthoringGuidePlaceholder>().FindAsync(
            x => ids.Contains(x.ItemId) && !x.IsDeleted, cancellationToken: ct)).ToLookup(x => x.ItemId);
        return rows.OrderBy(x => x.Order).ThenBy(x => x.GuideKey, StringComparer.Ordinal)
            .Select(x => ToGuide(x, audienceRole, blanks[x.Id])).ToArray();
    }

    public Task<int> CountAsync(int versionId, CancellationToken ct) =>
        uow.Repository<AuthoringGuideItem>().CountAsync(x => x.CatalogVersionId == versionId && !x.IsDeleted, ct);

    /// <summary>Thêm mục; gọi <paramref name="save"/> trước khi ghi chỗ trống vì dòng chỗ trống cần Id của mục.</summary>
    public async Task AddAsync(int versionId, IReadOnlyCollection<AuthoringGuide> guides, Func<Task> save, CancellationToken ct)
    {
        var pairs = guides.Select(g => (Guide: g, Row: Apply(new AuthoringGuideItem { CatalogVersionId = versionId }, g))).ToList();
        await uow.Repository<AuthoringGuideItem>().AddRangeAsync(pairs.Select(x => x.Row), ct);
        await save();
        await AddBlanksAsync(pairs, save, ct);
    }

    /// <summary>Sửa tại chỗ mục cùng mã (thêm nếu chưa có) và thay toàn bộ chỗ trống của nó.</summary>
    public async Task<AuthoringGuide?> UpsertAsync(int versionId, string audienceRole, AuthoringGuide guide, Func<Task> save,
        CancellationToken ct)
    {
        var row = await FindRowAsync(versionId, guide.Id, ct);
        if (row is null)
        {
            await AddAsync(versionId, [guide], save, ct);
            return null;
        }

        var oldBlanks = await BlanksOfAsync(row.Id, ct);
        var before = ToGuide(row, audienceRole, oldBlanks);
        uow.Repository<AuthoringGuideItem>().Update(Apply(row, guide));
        SoftDelete(oldBlanks);
        await save(); // chỗ trống cũ phải rời unique index trước khi thêm chỗ trống cùng key
        await AddBlanksAsync([(guide, row)], save, ct);
        return before;
    }

    /// <summary>Xóa mềm mục và các chỗ trống của nó; không lưu.</summary>
    public async Task<AuthoringGuide?> RemoveAsync(int versionId, string audienceRole, string guideKey, CancellationToken ct)
    {
        var row = await FindRowAsync(versionId, guideKey, ct);
        if (row is null) return null;
        var blanks = await BlanksOfAsync(row.Id, ct);
        var removed = ToGuide(row, audienceRole, blanks);
        row.IsDeleted = true;
        uow.Repository<AuthoringGuideItem>().Update(row);
        SoftDelete(blanks);
        return removed;
    }

    private async Task<AuthoringGuideItem?> FindRowAsync(int versionId, string guideKey, CancellationToken ct) =>
        (await uow.Repository<AuthoringGuideItem>().FindAsync(
            x => x.CatalogVersionId == versionId && x.GuideKey == guideKey && !x.IsDeleted, cancellationToken: ct)).SingleOrDefault();

    private Task<IReadOnlyList<AuthoringGuidePlaceholder>> BlanksOfAsync(int itemId, CancellationToken ct) =>
        uow.Repository<AuthoringGuidePlaceholder>().FindAsync(x => x.ItemId == itemId && !x.IsDeleted, cancellationToken: ct);

    private void SoftDelete(IEnumerable<AuthoringGuidePlaceholder> blanks)
    {
        foreach (var blank in blanks)
        {
            blank.IsDeleted = true;
            uow.Repository<AuthoringGuidePlaceholder>().Update(blank);
        }
    }

    private async Task AddBlanksAsync(IEnumerable<(AuthoringGuide Guide, AuthoringGuideItem Row)> pairs, Func<Task> save, CancellationToken ct)
    {
        var blanks = pairs.SelectMany(x => x.Guide.PlaceholderDefinitions.Select((p, i) => new AuthoringGuidePlaceholder
        {
            ItemId = x.Row.Id, Key = p.Key, Label = p.Label, Hint = p.Hint, Order = i + 1
        })).ToList();
        if (blanks.Count == 0) return;
        await uow.Repository<AuthoringGuidePlaceholder>().AddRangeAsync(blanks, ct);
        await save();
    }

    private static AuthoringGuideItem Apply(AuthoringGuideItem row, AuthoringGuide guide)
    {
        row.GuideKey = guide.Id;
        row.ItemType = guide.ItemType;
        row.SupportGroup = guide.SupportGroup;
        row.Purposes = [.. guide.Purposes];
        row.Title = guide.Title;
        row.Instruction = guide.Instruction;
        row.Example = guide.Example;
        row.TemplateText = guide.TemplateText;
        row.Order = guide.Order;
        return row;
    }

    private static AuthoringGuide ToGuide(AuthoringGuideItem row, string audienceRole, IEnumerable<AuthoringGuidePlaceholder> blanks) =>
        new(row.GuideKey, audienceRole, [.. row.Purposes], row.SupportGroup, row.ItemType, row.Title, row.Instruction, row.Example,
            row.TemplateText, blanks.OrderBy(x => x.Order).Select(x => new GuidePlaceholder(x.Key, x.Label, x.Hint)).ToArray(), row.Order);
}
