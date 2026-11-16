using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class StorySummaryConfiguration : IEntityTypeConfiguration<StorySummary>
{
    public void Configure(EntityTypeBuilder<StorySummary> builder)
    {
        builder.ToTable("story_summaries");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.AudioStoragePath).HasMaxLength(500);
        builder.Property(x => x.AudioContentType).HasMaxLength(50);

        builder.HasOne(x => x.ReadingSession).WithMany().HasForeignKey(x => x.ReadingSessionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ChildProfile).WithMany().HasForeignKey(x => x.ChildProfileId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Story).WithMany().HasForeignKey(x => x.StoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.ReadingSessionId)
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false")
            .HasDatabaseName("UX_story_summaries_one_per_session");
        builder.HasIndex(x => new { x.ChildProfileId, x.CreatedAt })
            .HasDatabaseName("IX_story_summaries_child_created");

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}

public class StorySummaryFlagConfiguration : IEntityTypeConfiguration<StorySummaryFlag>
{
    public void Configure(EntityTypeBuilder<StorySummaryFlag> builder)
    {
        builder.ToTable("story_summary_flags");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.QuestionText).HasMaxLength(200);
        builder.Property(x => x.AudioStoragePath).HasMaxLength(500);
        builder.Property(x => x.AudioContentType).HasMaxLength(50);

        builder.HasOne(x => x.StorySummary).WithMany().HasForeignKey(x => x.StorySummaryId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.StoryScene).WithMany().HasForeignKey(x => x.StorySceneId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.IllustrationBeat).WithMany().HasForeignKey(x => x.IllustrationBeatId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.MediaAsset).WithMany().HasForeignKey(x => x.MediaAssetId)
            .OnDelete(DeleteBehavior.SetNull);

        // Mỗi bản tóm tắt: một câu cho cả cảnh (khi không chọn tranh), một câu cho mỗi tranh.
        builder.HasIndex(x => new { x.StorySummaryId, x.StorySceneId })
            .IsUnique()
            .HasFilter("\"IllustrationBeatId\" IS NULL")
            .HasDatabaseName("UX_story_summary_flags_summary_scene");
        builder.HasIndex(x => new { x.StorySummaryId, x.IllustrationBeatId })
            .IsUnique()
            .HasFilter("\"IllustrationBeatId\" IS NOT NULL")
            .HasDatabaseName("UX_story_summary_flags_summary_beat");

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
