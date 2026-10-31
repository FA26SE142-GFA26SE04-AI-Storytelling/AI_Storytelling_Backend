using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class AuthoringGuideCatalogVersionConfiguration : IEntityTypeConfiguration<AuthoringGuideCatalogVersion>
{
    public void Configure(EntityTypeBuilder<AuthoringGuideCatalogVersion> builder)
    {
        builder.ToTable("authoring_guide_catalog_versions");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.CatalogVersion).IsRequired().HasMaxLength(50);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(x => x.ContentJson).HasColumnType("jsonb");

        // Bản nháp đã hủy (xóa mềm) không giữ chỗ số phiên bản / mã của nó; phiên bản đã phát hành không bao giờ bị xóa nên mã của chúng không bị tái sử dụng.
        builder.HasIndex(x => new { x.CatalogId, x.VersionNo }).IsUnique().HasFilter("NOT \"IsDeleted\"");
        builder.HasIndex(x => new { x.CatalogId, x.CatalogVersion }).IsUnique().HasFilter("NOT \"IsDeleted\"");

        // Mỗi bộ chỉ có tối đa một bản nháp.
        builder.HasIndex(x => x.CatalogId)
            .IsUnique()
            .HasFilter("\"Status\" = 'Draft' AND NOT \"IsDeleted\"");

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
