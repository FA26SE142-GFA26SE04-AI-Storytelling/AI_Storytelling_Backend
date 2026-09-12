using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class RecommendationReviewConfiguration : IEntityTypeConfiguration<RecommendationReview>
{
    public void Configure(EntityTypeBuilder<RecommendationReview> builder)
    {
        builder.ToTable("recommendation_reviews");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Decision)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(x => x.ModifiedValue).HasColumnType("text");
        builder.Property(x => x.Reason).HasColumnType("text");

        builder.HasOne(x => x.Recommendation)
            .WithMany()
            .HasForeignKey(x => x.RecommendationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ReviewerUser)
            .WithMany()
            .HasForeignKey(x => x.ReviewerUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
