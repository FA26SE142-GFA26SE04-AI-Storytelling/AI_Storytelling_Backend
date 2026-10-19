using System.Text.Json;
using System.Text.RegularExpressions;
using StoryPlatform.Application.Common.Exceptions;

namespace StoryPlatform.Application.Features.ParentAuthoring;

public sealed record AuthoringGuide(string Id, string AudienceRole, string[] Purposes, string SupportGroup,
    string ItemType, string Title, string Instruction, string? Example, string? TemplateText, string[] PlaceholderDefinitions, int Order);
public sealed record GuideCatalog(int SchemaVersion, string CatalogVersion, string Locale, AuthoringGuide[] Items);
public sealed record GuidePage(string CatalogVersion, string Locale, int PageIndex, int PageSize, int TotalCount, AuthoringGuide[] Items);
public sealed record RenderGuideRequest(Dictionary<string, string>? Values);
public sealed record RenderGuideResult(string CatalogVersion, string GuideId, string Text, bool IsPlainText = true);

/// <summary>Shared, deterministic content; never reads a draft, child profile or provider.</summary>
public sealed class AuthoringGuideService
{
    private readonly GuideCatalog _catalog;
    public AuthoringGuideService()
    {
        using var source = typeof(AuthoringGuideService).Assembly.GetManifestResourceStream("ParentAuthoringGuides.vi.json")
            ?? throw new InvalidOperationException("Guide catalog missing");
        _catalog = JsonSerializer.Deserialize<GuideCatalog>(source, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("Guide catalog invalid");
        if (_catalog.SchemaVersion != 1 || _catalog.Locale != "vi" || string.IsNullOrWhiteSpace(_catalog.CatalogVersion) ||
            _catalog.Items.Select(x => x.Id).Distinct().Count() != _catalog.Items.Length ||
            _catalog.Items.Any(x => x.AudienceRole != "Parent" || x.Purposes.Any(p => !ParentManualAuthoringPolicy.Purposes.Contains(p)) ||
                !Regex.Matches(x.TemplateText ?? "", @"\{\{([a-z_]+)\}\}").Select(m => m.Groups[1].Value).Distinct().Order()
                    .SequenceEqual(x.PlaceholderDefinitions.Distinct().Order())))
            throw new InvalidOperationException("Guide catalog schema validation failed");
    }

    public GuidePage Get(string locale, string? purpose, string? group, int pageIndex, int pageSize)
    {
        if (pageIndex < 1 || pageSize is < 1 or > 100) throw new BadRequestException("PAGINATION_INVALID");
        ParentManualAuthoringPolicy.ValidatePurpose(purpose);
        if (locale != _catalog.Locale) return new(_catalog.CatalogVersion, locale, pageIndex, pageSize, 0, []);
        var all = _catalog.Items.Where(x => group is null || x.SupportGroup == group).ToArray();
        var filtered = all.Where(x => x.Purposes.Contains(purpose ?? "free")).ToArray();
        if (filtered.Length == 0) filtered = all.Where(x => x.Purposes.Contains("free")).ToArray();
        var ordered = filtered.OrderBy(x => x.Order).ThenBy(x => x.Id, StringComparer.Ordinal).ToArray();
        return new(_catalog.CatalogVersion, locale, pageIndex, pageSize, ordered.Length,
            ordered.Skip((pageIndex - 1) * pageSize).Take(pageSize).ToArray());
    }

    public RenderGuideResult Render(string id, RenderGuideRequest request)
    {
        var guide = _catalog.Items.SingleOrDefault(x => x.Id == id) ?? throw new NotFoundException("Guide", id);
        if (guide.TemplateText is null) throw new BadRequestException("GUIDE_HAS_NO_TEMPLATE");
        var values = request.Values ?? [];
        if (values.Keys.Except(guide.PlaceholderDefinitions).Any() || guide.PlaceholderDefinitions.Any(x =>
                !values.TryGetValue(x, out var value) || string.IsNullOrWhiteSpace(value) || value.Length > 500))
            throw new BadRequestException("PLACEHOLDER_VALUES_INVALID");
        // One pass: inserted values are text, never recursively interpreted as template syntax/HTML.
        var text = Regex.Replace(guide.TemplateText, @"\{\{([a-z_]+)\}\}", m => values[m.Groups[1].Value]);
        return new(_catalog.CatalogVersion, guide.Id, text);
    }
}
