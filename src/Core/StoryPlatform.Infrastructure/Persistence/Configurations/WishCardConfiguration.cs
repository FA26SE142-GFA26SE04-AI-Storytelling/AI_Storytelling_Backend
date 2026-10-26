using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class WishCardConfiguration : IEntityTypeConfiguration<WishCard>
{
    public void Configure(EntityTypeBuilder<WishCard> builder)
    {
        builder.ToTable("wish_cards");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Group)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(x => x.Label)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.ImageUrl)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(x => x.AudioUrl)
            .HasMaxLength(500);

        builder.Property(x => x.AgeBand)
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.HasOne(x => x.ContentCategory)
            .WithMany()
            .HasForeignKey(x => x.ContentCategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.Group, x.IsActive, x.SortOrder });

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
