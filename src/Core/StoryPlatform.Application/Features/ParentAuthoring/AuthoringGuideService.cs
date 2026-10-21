using System.Text.Json;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ParentAuthoring;

public sealed record AuthoringGuide(string Id, string AudienceRole, string[] Purposes, string SupportGroup,
    string ItemType, string Title, string Instruction, string? Example, string? TemplateText, string[] PlaceholderDefinitions, int Order);
public sealed record GuideCatalog(int SchemaVersion, string CatalogVersion, string Locale, AuthoringGuide[] Items);
public sealed record GuidePage(string CatalogVersion, string Locale, int PageIndex, int PageSize, int TotalCount, AuthoringGuide[] Items);
public sealed record RenderGuideRequest(Dictionary<string, string>? Values, string? CatalogVersion = null);
public sealed record RenderGuideResult(string CatalogVersion, string GuideId, string Text, bool IsPlainText = true);

/// <summary>Shared, deterministic content; reads only the published version, never a draft, child profile or provider.</summary>
public sealed class AuthoringGuideService(IUnitOfWork uow)
{
    public async Task<GuidePage> GetAsync(string locale, string? purpose, string? group, int pageIndex, int pageSize,
        CancellationToken ct = default)
    {
        if (pageIndex < 1 || pageSize is < 1 or > 100) throw new BadRequestException("PAGINATION_INVALID");
        ParentManualAuthoringPolicy.ValidatePurpose(purpose);
        var (catalogVersion, catalogLocale, items) = await LoadPublishedAsync(ct);
        if (locale != catalogLocale) return new(catalogVersion, locale, pageIndex, pageSize, 0, []);
        var all = items.Where(x => group is null || x.SupportGroup == group).ToArray();
        var filtered = all.Where(x => x.Purposes.Contains(purpose ?? "free")).ToArray();
        if (filtered.Length == 0) filtered = all.Where(x => x.Purposes.Contains("free")).ToArray();
        var ordered = filtered.OrderBy(x => x.Order).ThenBy(x => x.Id, StringComparer.Ordinal).ToArray();
        return new(catalogVersion, locale, pageIndex, pageSize, ordered.Length,
            ordered.Skip((pageIndex - 1) * pageSize).Take(pageSize).ToArray());
    }

    public async Task<RenderGuideResult> RenderAsync(string id, RenderGuideRequest request, CancellationToken ct = default)
    {
        var (catalogVersion, _, items) = await LoadPublishedAsync(ct);
        if (!string.IsNullOrWhiteSpace(request.CatalogVersion) && request.CatalogVersion != catalogVersion)
            throw new ConflictException("GUIDE_CATALOG_CHANGED");
        var guide = items.SingleOrDefault(x => x.Id == id) ?? throw new NotFoundException("Guide", id);
        if (guide.TemplateText is null) throw new BadRequestException("GUIDE_HAS_NO_TEMPLATE");
        var values = request.Values ?? [];
        if (values.Keys.Except(guide.PlaceholderDefinitions).Any() || guide.PlaceholderDefinitions.Any(x =>
                !values.TryGetValue(x, out var value) || string.IsNullOrWhiteSpace(value) || value.Length > 500))
            throw new BadRequestException("PLACEHOLDER_VALUES_INVALID");
        // One pass: inserted values are text, never recursively interpreted as template syntax/HTML.
        var text = AuthoringGuideCatalogValidator.PlaceholderPattern.Replace(guide.TemplateText, m => values[m.Groups[1].Value]);
        return new(catalogVersion, guide.Id, text);
    }

    private async Task<(string CatalogVersion, string Locale, AuthoringGuide[] Items)> LoadPublishedAsync(CancellationToken ct)
    {
        var catalog = await uow.Repository<AuthoringGuideCatalog>().FirstOrDefaultAsync(
            x => x.Locale == AuthoringGuideCatalogSeeder.DefaultLocale && x.AudienceRole == AuthoringGuideCatalogSeeder.DefaultAudience,
            cancellationToken: ct);
        var versionId = catalog?.PublishedVersionId ?? throw new ConflictException("GUIDE_CATALOG_UNAVAILABLE");
        var version = await uow.Repository<AuthoringGuideCatalogVersion>().FirstOrDefaultAsync(
            x => x.Id == versionId && x.Status == AuthoringGuideVersionStatus.Published, cancellationToken: ct)
            ?? throw new ConflictException("GUIDE_CATALOG_UNAVAILABLE");
        var content = JsonSerializer.Deserialize<AuthoringGuideCatalogContent>(version.ContentJson, AuthoringGuideCatalogSeeder.Json)
            ?? throw new ConflictException("GUIDE_CATALOG_UNAVAILABLE");
        return (version.CatalogVersion, catalog!.Locale, content.Items);
    }
}
