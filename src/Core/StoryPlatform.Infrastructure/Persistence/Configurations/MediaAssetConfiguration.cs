using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class MediaAssetConfiguration : IEntityTypeConfiguration<MediaAsset>
{
    public void Configure(EntityTypeBuilder<MediaAsset> builder)
    {
        builder.ToTable("media_assets", table =>
        {
            table.HasCheckConstraint("CK_media_assets_attempt_count", "\"AttemptCount\" >= 0");
            table.HasCheckConstraint(
                "CK_media_assets_segment_required_for_audio",
                "\"Type\" <> 'TtsAudio' OR \"StorySegmentId\" IS NOT NULL");
            table.HasCheckConstraint(
                "CK_media_assets_segment_must_be_null_for_illustration",
                "\"Type\" <> 'Illustration' OR \"StorySegmentId\" IS NULL");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Type)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(x => x.ValidationStatus)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(x => x.Url).HasMaxLength(500);
        builder.Property(x => x.MimeType).HasMaxLength(100);
        builder.Property(x => x.Provider).HasMaxLength(50);
        builder.Property(x => x.Model).HasMaxLength(120);
        builder.Property(x => x.ProviderRequestId).HasMaxLength(120);
        builder.Property(x => x.ProviderAssetId).HasMaxLength(200);
        builder.Property(x => x.WordTimings).HasColumnType("text");
        builder.Property(x => x.ValidationResultJson).HasColumnType("text");
        builder.Property(x => x.LastValidationReason).HasMaxLength(500);

        builder.HasOne(x => x.StoryVersion)
            .WithMany()
            .HasForeignKey(x => x.StoryVersionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.StoryScene)
            .WithMany()
            .HasForeignKey(x => x.StorySceneId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.StorySegment)
            .WithMany()
            .HasForeignKey(x => x.StorySegmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.StorySceneId, x.Type })
            .IsUnique()
            .HasFilter("\"StorySceneId\" IS NOT NULL AND \"StorySegmentId\" IS NULL AND \"IsDeleted\" = false");

        builder.HasIndex(x => new { x.StorySegmentId, x.Type })
            .IsUnique()
            .HasFilter("\"StorySegmentId\" IS NOT NULL AND \"IsDeleted\" = false");

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
