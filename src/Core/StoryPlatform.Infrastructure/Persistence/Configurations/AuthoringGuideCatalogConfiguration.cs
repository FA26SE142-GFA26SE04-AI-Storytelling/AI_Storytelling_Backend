using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class AuthoringGuideCatalogConfiguration : IEntityTypeConfiguration<AuthoringGuideCatalog>
{
    public void Configure(EntityTypeBuilder<AuthoringGuideCatalog> builder)
    {
        builder.ToTable("authoring_guide_catalogs");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Locale).IsRequired().HasMaxLength(10);
        builder.Property(x => x.AudienceRole).IsRequired().HasMaxLength(30);
        builder.Property(x => x.RevisionToken).IsRequired().HasMaxLength(64).IsConcurrencyToken();

        builder.HasIndex(x => new { x.Locale, x.AudienceRole }).IsUnique();

        builder.HasMany(x => x.Versions)
            .WithOne(x => x.Catalog)
            .HasForeignKey(x => x.CatalogId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<AuthoringGuideCatalogVersion>()
            .WithMany()
            .HasForeignKey(x => x.PublishedVersionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<AuthoringGuideCatalogVersion>()
            .WithMany()
            .HasForeignKey(x => x.DraftVersionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
