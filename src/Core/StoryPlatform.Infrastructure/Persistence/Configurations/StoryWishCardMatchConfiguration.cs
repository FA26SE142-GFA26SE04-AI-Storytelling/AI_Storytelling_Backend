using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class StoryWishCardMatchConfiguration : IEntityTypeConfiguration<StoryWishCardMatch>
{
    public void Configure(EntityTypeBuilder<StoryWishCardMatch> builder)
    {
        builder.ToTable("story_wish_card_matches");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Source)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.HasOne(x => x.Story)
            .WithMany()
            .HasForeignKey(x => x.StoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.WishCard)
            .WithMany()
            .HasForeignKey(x => x.WishCardId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.StoryId, x.WishCardId })
            .IsUnique();

        builder.HasIndex(x => x.WishCardId);

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
