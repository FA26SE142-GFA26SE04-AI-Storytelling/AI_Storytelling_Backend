using Microsoft.EntityFrameworkCore;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.Infrastructure.Persistence;
using Xunit;

namespace StoryPlatform.IntegrationTests;

public sealed class AuthoringGuideCatalogModelTests
{
    [Fact]
    public void Catalog_is_unique_per_locale_and_audience_with_concurrency_token()
    {
        using var context = CreateModelOnlyContext();
        var catalog = context.Model.FindEntityType(typeof(AuthoringGuideCatalog))!;

        Assert.Equal("authoring_guide_catalogs", catalog.GetTableName());
        Assert.True(catalog.FindProperty(nameof(AuthoringGuideCatalog.RevisionToken))!.IsConcurrencyToken);
        Assert.Contains(catalog.GetIndexes(), index => index.IsUnique &&
            index.Properties.Select(p => p.Name).SequenceEqual(
                [nameof(AuthoringGuideCatalog.Locale), nameof(AuthoringGuideCatalog.AudienceRole)]));
        Assert.Equal(2, catalog.GetForeignKeys().Count(fk =>
            fk.PrincipalEntityType.ClrType == typeof(AuthoringGuideCatalogVersion) &&
            fk.DeleteBehavior == DeleteBehavior.Restrict));
    }

    [Fact]
    public void Version_stores_content_as_jsonb_with_unique_number_and_single_draft()
    {
        using var context = CreateModelOnlyContext();
        var version = context.Model.FindEntityType(typeof(AuthoringGuideCatalogVersion))!;

        Assert.Equal("authoring_guide_catalog_versions", version.GetTableName());
        Assert.Equal("jsonb", version.FindProperty(nameof(AuthoringGuideCatalogVersion.ContentJson))!.GetColumnType());
        Assert.False(version.FindProperty(nameof(AuthoringGuideCatalogVersion.ContentJson))!.IsNullable);
        Assert.Equal(typeof(string), version.FindProperty(nameof(AuthoringGuideCatalogVersion.Status))!.GetProviderClrType());
        Assert.Contains(version.GetIndexes(), index => index.IsUnique &&
            index.Properties.Select(p => p.Name).SequenceEqual(
                [nameof(AuthoringGuideCatalogVersion.CatalogId), nameof(AuthoringGuideCatalogVersion.VersionNo)]));
        Assert.Contains(version.GetIndexes(), index => index.IsUnique &&
            index.Properties.Select(p => p.Name).SequenceEqual(
                [nameof(AuthoringGuideCatalogVersion.CatalogId), nameof(AuthoringGuideCatalogVersion.CatalogVersion)]));
        Assert.Contains(version.GetIndexes(), index => index.IsUnique && index.GetFilter() is not null &&
            index.Properties.Select(p => p.Name).SequenceEqual([nameof(AuthoringGuideCatalogVersion.CatalogId)]));
        Assert.Equal(AuthoringGuideVersionStatus.Draft, (AuthoringGuideVersionStatus)1);
    }

    private static ApplicationDbContext CreateModelOnlyContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=model_only;Password=model_only")
            .Options);
}
