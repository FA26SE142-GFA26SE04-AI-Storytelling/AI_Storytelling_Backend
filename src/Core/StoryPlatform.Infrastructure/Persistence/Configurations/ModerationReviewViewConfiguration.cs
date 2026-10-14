using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class ModerationReviewViewConfiguration : IEntityTypeConfiguration<ModerationReviewView>
{
    public const string HasTargetConstraintName = "ck_moderation_review_views_has_target";
    public const string HasTargetSql = "\"StoryId\" IS NOT NULL OR \"ReadingRoadmapId\" IS NOT NULL";

    public void Configure(EntityTypeBuilder<ModerationReviewView> builder)
    {
        builder.ToTable("moderation_review_views", table =>
            table.HasCheckConstraint(HasTargetConstraintName, HasTargetSql));

        builder.HasKey(x => x.Id);

        builder.Property(x => x.SourceTab).HasMaxLength(30).IsRequired();
        builder.Property(x => x.GrantedAt).IsRequired();
        builder.Property(x => x.ExpiresAt).IsRequired();

        builder.HasOne(x => x.ModeratorUser).WithMany().HasForeignKey(x => x.ModeratorUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Story).WithMany().HasForeignKey(x => x.StoryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.StoryVersion).WithMany().HasForeignKey(x => x.StoryVersionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ReadingRoadmap).WithMany().HasForeignKey(x => x.ReadingRoadmapId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.ModeratorUserId, x.StoryId, x.ExpiresAt });
        builder.HasIndex(x => new { x.ModeratorUserId, x.ReadingRoadmapId, x.ExpiresAt });

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
