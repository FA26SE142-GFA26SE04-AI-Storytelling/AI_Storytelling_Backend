using System.Text.Json;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ParentAuthoring;

/// <summary>Nạp bộ hướng dẫn nhúng trong ứng dụng vào database đúng một lần; không bao giờ ghi đè dữ liệu đã có.</summary>
public sealed class AuthoringGuideCatalogSeeder(IUnitOfWork uow)
{
    public const string DefaultLocale = "vi";
    public const string DefaultAudience = "Parent";
    public const int SchemaVersion = 2;
    private const int SeedLockResourceId = 0x41475344; // "AGSD"
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static GuideCatalog LoadEmbedded()
    {
        using var source = typeof(AuthoringGuideCatalogSeeder).Assembly.GetManifestResourceStream("ParentAuthoringGuides.vi.json")
            ?? throw new InvalidOperationException("Guide catalog missing");
        var catalog = JsonSerializer.Deserialize<GuideCatalog>(source, Json)
            ?? throw new InvalidOperationException("Guide catalog invalid");
        if (catalog.SchemaVersion != SchemaVersion || catalog.Locale != DefaultLocale || string.IsNullOrWhiteSpace(catalog.CatalogVersion))
            throw new InvalidOperationException("Guide catalog schema validation failed");
        AuthoringGuideCatalogValidator.Validate(catalog.Items, requireLabels: true);
        return catalog;
    }

    public async Task<bool> SeedAsync(CancellationToken ct)
    {
        await uow.BeginTransactionAsync(ct);
        try
        {
            await uow.AcquireTransactionLockAsync(SeedLockResourceId, ct);
            var catalogs = uow.Repository<AuthoringGuideCatalog>();
            if (await catalogs.ExistsAsync(x => x.Locale == DefaultLocale && x.AudienceRole == DefaultAudience, ct))
            {
                await uow.CommitTransactionAsync(ct);
                return false;
            }

            var source = LoadEmbedded();
            var catalog = await catalogs.AddAsync(new AuthoringGuideCatalog { Locale = source.Locale, AudienceRole = DefaultAudience }, ct);
            await uow.SaveChangesAsync(ct);

            var version = await uow.Repository<AuthoringGuideCatalogVersion>().AddAsync(new AuthoringGuideCatalogVersion
            {
                CatalogId = catalog.Id,
                VersionNo = 1,
                CatalogVersion = source.CatalogVersion,
                Status = AuthoringGuideVersionStatus.Published,
                PublishedAt = DateTime.UtcNow
            }, ct);
            await uow.SaveChangesAsync(ct);
            await new AuthoringGuideItemStore(uow).AddAsync(version.Id, source.Items, () => uow.SaveChangesAsync(ct), ct);

            catalog.PublishedVersionId = version.Id;
            catalog.RevisionToken = Guid.NewGuid().ToString("N");
            await uow.SaveChangesAsync(ct);
            await uow.CommitTransactionAsync(ct);
            return true;
        }
        catch
        {
            await uow.RollbackTransactionAsync(CancellationToken.None);
            throw;
        }
    }
}
