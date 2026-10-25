using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class StoryWishSuggestionConfiguration : IEntityTypeConfiguration<StoryWishSuggestion>
{
    public void Configure(EntityTypeBuilder<StoryWishSuggestion> builder)
    {
        builder.ToTable("story_wish_suggestions");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.CommunitySubmissionId).IsRequired(false);

        builder.Property(x => x.MatchedCardIds)
            .IsRequired()
            .HasMaxLength(100);

        builder.HasOne(x => x.StoryWish)
            .WithMany()
            .HasForeignKey(x => x.StoryWishId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Story)
            .WithMany()
            .HasForeignKey(x => x.StoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.StoryWishId, x.Rank })
            .IsUnique();

        builder.HasIndex(x => new { x.StoryWishId, x.StoryId })
            .IsUnique();

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
