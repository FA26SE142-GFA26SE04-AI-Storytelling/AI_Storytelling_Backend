using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class PromptCatalogVersionConfiguration : IEntityTypeConfiguration<PromptCatalogVersion>
{
    public void Configure(EntityTypeBuilder<PromptCatalogVersion> builder)
    {
        builder.ToTable("prompt_catalog_versions");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.VersionNo)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(x => x.GradeBand)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(x => x.RestrictedKeywords)
            .HasColumnType("text");

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.HasOne(x => x.CreatedByAdmin)
            .WithMany()
            .HasForeignKey(x => x.CreatedByAdminId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
