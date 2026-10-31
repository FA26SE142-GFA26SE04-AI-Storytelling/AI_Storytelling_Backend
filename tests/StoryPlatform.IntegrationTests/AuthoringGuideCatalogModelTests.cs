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
        Assert.True(version.FindProperty(nameof(AuthoringGuideCatalogVersion.ContentJson))!.IsNullable);
        Assert.Equal(typeof(string), version.FindProperty(nameof(AuthoringGuideCatalogVersion.Status))!.GetProviderClrType());
        // Soft-deleted (discarded) drafts must not block reusing their number / code.
        Assert.Contains(version.GetIndexes(), index => index.IsUnique && index.GetFilter() == "NOT \"IsDeleted\"" &&
            index.Properties.Select(p => p.Name).SequenceEqual(
                [nameof(AuthoringGuideCatalogVersion.CatalogId), nameof(AuthoringGuideCatalogVersion.VersionNo)]));
        Assert.Contains(version.GetIndexes(), index => index.IsUnique && index.GetFilter() == "NOT \"IsDeleted\"" &&
            index.Properties.Select(p => p.Name).SequenceEqual(
                [nameof(AuthoringGuideCatalogVersion.CatalogId), nameof(AuthoringGuideCatalogVersion.CatalogVersion)]));
        Assert.Contains(version.GetIndexes(), index => index.IsUnique &&
            index.GetFilter() == "\"Status\" = 'Draft' AND NOT \"IsDeleted\"" &&
            index.Properties.Select(p => p.Name).SequenceEqual([nameof(AuthoringGuideCatalogVersion.CatalogId)]));
        Assert.Equal(AuthoringGuideVersionStatus.Draft, (AuthoringGuideVersionStatus)1);
    }

    [Fact]
    public void Items_are_rows_of_a_version_with_a_unique_live_key_and_text_array_purposes()
    {
        using var context = CreateModelOnlyContext();
        var item = context.Model.FindEntityType(typeof(AuthoringGuideItem))!;
        Assert.Equal("authoring_guide_items", item.GetTableName());
        Assert.Equal("text[]", item.FindProperty(nameof(AuthoringGuideItem.Purposes))!.GetColumnType());
        Assert.Equal(100, item.FindProperty(nameof(AuthoringGuideItem.GuideKey))!.GetMaxLength());
        Assert.Equal(20, item.FindProperty(nameof(AuthoringGuideItem.ItemType))!.GetMaxLength());
        Assert.Equal(30, item.FindProperty(nameof(AuthoringGuideItem.SupportGroup))!.GetMaxLength());
        Assert.Contains(item.GetIndexes(), index => index.IsUnique && index.GetFilter() == "NOT \"IsDeleted\"" &&
            index.Properties.Select(p => p.Name).SequenceEqual([nameof(AuthoringGuideItem.CatalogVersionId), nameof(AuthoringGuideItem.GuideKey)]));
    }

    [Fact]
    public void Placeholders_are_rows_of_an_item_with_a_unique_live_key_and_bounded_labels()
    {
        using var context = CreateModelOnlyContext();
        var placeholder = context.Model.FindEntityType(typeof(AuthoringGuidePlaceholder))!;
        Assert.Equal("authoring_guide_placeholders", placeholder.GetTableName());
        Assert.Equal(50, placeholder.FindProperty(nameof(AuthoringGuidePlaceholder.Key))!.GetMaxLength());
        Assert.Equal(100, placeholder.FindProperty(nameof(AuthoringGuidePlaceholder.Label))!.GetMaxLength());
        Assert.Equal(200, placeholder.FindProperty(nameof(AuthoringGuidePlaceholder.Hint))!.GetMaxLength());
        Assert.True(placeholder.FindProperty(nameof(AuthoringGuidePlaceholder.Label))!.IsNullable);
        Assert.Contains(placeholder.GetIndexes(), index => index.IsUnique && index.GetFilter() == "NOT \"IsDeleted\"" &&
            index.Properties.Select(p => p.Name).SequenceEqual([nameof(AuthoringGuidePlaceholder.ItemId), nameof(AuthoringGuidePlaceholder.Key)]));
    }

    private static ApplicationDbContext CreateModelOnlyContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=model_only;Password=model_only")
            .Options);
}
