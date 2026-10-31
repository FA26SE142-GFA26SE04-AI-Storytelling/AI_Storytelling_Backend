using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class AuthoringGuideItemConfiguration : IEntityTypeConfiguration<AuthoringGuideItem>
{
    public void Configure(EntityTypeBuilder<AuthoringGuideItem> builder)
    {
        builder.ToTable("authoring_guide_items");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.GuideKey).IsRequired().HasMaxLength(100);
        builder.Property(x => x.ItemType).IsRequired().HasMaxLength(20);
        builder.Property(x => x.SupportGroup).IsRequired().HasMaxLength(30);
        builder.Property(x => x.Purposes).HasColumnType("text[]").IsRequired();
        builder.Property(x => x.Title).IsRequired();
        builder.Property(x => x.Instruction).IsRequired();

        builder.HasOne<AuthoringGuideCatalogVersion>()
            .WithMany()
            .HasForeignKey(x => x.CatalogVersionId)
            .OnDelete(DeleteBehavior.Restrict);

        // Mục đã xóa mềm không giữ chỗ mã của nó trong phiên bản.
        builder.HasIndex(x => new { x.CatalogVersionId, x.GuideKey }).IsUnique().HasFilter("NOT \"IsDeleted\"");

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
