using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class AuthoringGuidePlaceholderConfiguration : IEntityTypeConfiguration<AuthoringGuidePlaceholder>
{
    public void Configure(EntityTypeBuilder<AuthoringGuidePlaceholder> builder)
    {
        builder.ToTable("authoring_guide_placeholders");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Key).IsRequired().HasMaxLength(50);
        builder.Property(x => x.Label).HasMaxLength(100);
        builder.Property(x => x.Hint).HasMaxLength(200);

        builder.HasOne<AuthoringGuideItem>()
            .WithMany()
            .HasForeignKey(x => x.ItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.ItemId, x.Key }).IsUnique().HasFilter("NOT \"IsDeleted\"");

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
