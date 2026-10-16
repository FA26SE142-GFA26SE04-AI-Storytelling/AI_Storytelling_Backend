using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class ModerationItemConfiguration : IEntityTypeConfiguration<ModerationItem>
{
    public const string ClaimerIsNotCreatorConstraintName = "ck_moderation_items_claimer_is_not_creator";

    public const string ClaimerIsNotCreatorSql =
        "\"ClaimedByUserId\" IS NULL OR \"CreatedByUserId\" IS NULL OR \"ClaimedByUserId\" <> \"CreatedByUserId\"";

    public void Configure(EntityTypeBuilder<ModerationItem> builder)
    {
        builder.ToTable("moderation_items", table =>
            table.HasCheckConstraint(ClaimerIsNotCreatorConstraintName, ClaimerIsNotCreatorSql));

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ItemType).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(30).IsRequired()
            .HasDefaultValue(ModerationItemStatus.Pending)
            .HasSentinel(ModerationItemStatus.Pending);
        builder.Property(x => x.Priority).HasConversion<string>().HasMaxLength(30).IsRequired()
            .HasDefaultValue(ModerationPriority.Medium)
            .HasSentinel(ModerationPriority.Medium);
        builder.Property(x => x.CopyrightDeclaration).HasConversion<string>().HasMaxLength(30);

        builder.Property(x => x.PiiCheckResult).HasColumnType("jsonb");
        builder.Property(x => x.Flags).HasColumnType("jsonb");
        builder.Property(x => x.Checklist).HasColumnType("jsonb");
        builder.Property(x => x.ReviewProgress).HasColumnType("jsonb");
        builder.Property(x => x.Feedback).HasColumnType("jsonb");

        builder.Property(x => x.DecisionReason).HasColumnType("text");
        builder.Property(x => x.EscalationReason).HasMaxLength(1000);
        builder.Property(x => x.ClosedReason).HasMaxLength(50);
        builder.Property(x => x.RowVersion).IsConcurrencyToken();

        builder.HasOne(x => x.Story).WithMany().HasForeignKey(x => x.StoryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.StoryVersion).WithMany().HasForeignKey(x => x.StoryVersionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ContentReport).WithMany().HasForeignKey(x => x.ContentReportId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ReadingRoadmap).WithMany().HasForeignKey(x => x.ReadingRoadmapId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.SourceChildProfile).WithMany().HasForeignKey(x => x.SourceChildProfileId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.PreviousItem).WithMany().HasForeignKey(x => x.PreviousItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ClaimedByUser).WithMany().HasForeignKey(x => x.ClaimedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.DecidedByUser).WithMany().HasForeignKey(x => x.DecidedByUserId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.ItemType, x.Status, x.Priority });
        builder.HasIndex(x => new { x.ClaimedByUserId, x.Status });

        builder.HasIndex(x => x.StoryId)
            .IsUnique()
            .HasFilter("\"ItemType\" = 'CommunitySubmission' AND \"Status\" IN ('Pending', 'Approved') AND NOT \"IsDeleted\"");

        builder.HasIndex(x => x.ReadingRoadmapId)
            .IsUnique()
            .HasFilter("\"ItemType\" = 'RoadmapTemplateReview' AND \"Status\" IN ('Pending', 'Claimed') AND NOT \"IsDeleted\"");

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
