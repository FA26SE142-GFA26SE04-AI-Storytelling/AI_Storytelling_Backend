using StoryPlatform.Application.Common.Exceptions;

namespace StoryPlatform.Application.Features.ParentAuthoring;

/// <summary>Lọc, phân trang và điền mẫu trên một danh sách hướng dẫn; dùng chung cho API Parent và xem trước của Moderator.</summary>
public static class AuthoringGuideCatalogQuery
{
    public static void ValidatePaging(string? purpose, int pageIndex, int pageSize)
    {
        if (pageIndex < 1 || pageSize is < 1 or > 100) throw new BadRequestException("PAGINATION_INVALID");
        ParentManualAuthoringPolicy.ValidatePurpose(purpose);
    }

    public static GuidePage Page(string catalogVersion, string locale, string? purpose, string? group, int pageIndex, int pageSize,
        IReadOnlyCollection<AuthoringGuide> items)
    {
        var all = items.Where(x => group is null || x.SupportGroup == group).ToArray();
        var filtered = all.Where(x => x.Purposes.Contains(purpose ?? "free")).ToArray();
        if (filtered.Length == 0) filtered = all.Where(x => x.Purposes.Contains("free")).ToArray();
        var ordered = filtered.OrderBy(x => x.Order).ThenBy(x => x.Id, StringComparer.Ordinal).ToArray();
        return new(catalogVersion, locale, pageIndex, pageSize, ordered.Length,
            ordered.Skip((pageIndex - 1) * pageSize).Take(pageSize).ToArray());
    }

    public static string RenderText(AuthoringGuide guide, IReadOnlyDictionary<string, string>? values)
    {
        if (guide.TemplateText is null) throw new BadRequestException("GUIDE_HAS_NO_TEMPLATE");
        var keys = guide.PlaceholderDefinitions.Select(p => p.Key).ToArray();
        var map = values ?? new Dictionary<string, string>();
        if (map.Keys.Except(keys).Any() || keys.Any(x =>
                !map.TryGetValue(x, out var value) || string.IsNullOrWhiteSpace(value) || value.Length > 500))
            throw new BadRequestException("PLACEHOLDER_VALUES_INVALID");
        // One pass: inserted values are text, never recursively interpreted as template syntax/HTML.
        return AuthoringGuideCatalogValidator.PlaceholderPattern.Replace(guide.TemplateText, m => map[m.Groups[1].Value]);
    }
}
