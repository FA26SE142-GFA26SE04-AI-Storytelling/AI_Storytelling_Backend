using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class StoryRatingConfiguration : IEntityTypeConfiguration<StoryRating>
{
    public const string RatingRangeConstraintName = "ck_story_ratings_rating_range";

    public void Configure(EntityTypeBuilder<StoryRating> builder)
    {
        builder.ToTable("story_ratings", table =>
            table.HasCheckConstraint(RatingRangeConstraintName, "\"Rating\" BETWEEN 1 AND 5"));

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Comment).HasMaxLength(1000);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(x => x.HiddenReason).HasMaxLength(1000);

        builder.HasOne(x => x.Story).WithMany().HasForeignKey(x => x.StoryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ReviewerUser).WithMany().HasForeignKey(x => x.ReviewerUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ModeratedByUser).WithMany().HasForeignKey(x => x.ModeratedByUserId).OnDelete(DeleteBehavior.Restrict);

        // Xóa rồi đánh giá lại tạo dòng mới, nên unique chỉ áp cho dòng chưa xóa.
        builder.HasIndex(x => new { x.StoryId, x.ReviewerUserId }).IsUnique().HasFilter("NOT \"IsDeleted\"");
        builder.HasIndex(x => new { x.StoryId, x.Status });

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}

public class StoryRatingReportConfiguration : IEntityTypeConfiguration<StoryRatingReport>
{
    public void Configure(EntityTypeBuilder<StoryRatingReport> builder)
    {
        builder.ToTable("story_rating_reports");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Reason).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(x => x.Note).HasMaxLength(500);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(30).IsRequired();

        builder.HasOne(x => x.StoryRating).WithMany().HasForeignKey(x => x.StoryRatingId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ReporterUser).WithMany().HasForeignKey(x => x.ReporterUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ModerationItem).WithMany().HasForeignKey(x => x.ModerationItemId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.StoryRatingId, x.ReporterUserId }).IsUnique();
        builder.HasIndex(x => new { x.ModerationItemId, x.Status });

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
