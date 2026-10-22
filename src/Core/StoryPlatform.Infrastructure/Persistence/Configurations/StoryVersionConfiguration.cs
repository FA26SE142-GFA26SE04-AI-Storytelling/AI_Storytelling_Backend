using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class StoryVersionConfiguration : IEntityTypeConfiguration<StoryVersion>
{
    public void Configure(EntityTypeBuilder<StoryVersion> builder)
    {
        builder.ToTable("story_versions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.EditType).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(x => x.Title).IsRequired().HasMaxLength(200);
        builder.Property(x => x.OutlineOpening).HasColumnType("text");
        builder.Property(x => x.OutlineDevelopment).HasColumnType("text");
        builder.Property(x => x.OutlineEnding).HasColumnType("text");
        builder.Property(x => x.Content).HasColumnType("text");
        builder.Property(x => x.Lesson).HasColumnType("text");
        builder.Property(x => x.ReadabilityFkgl).HasColumnType("decimal(6,3)");
        builder.Property(x => x.ReadabilityFre).HasColumnType("decimal(6,3)");
        builder.Property(x => x.SafetyScore).HasColumnType("decimal(6,3)");
        builder.HasOne(x => x.Story).WithMany().HasForeignKey(x => x.StoryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.EditorUser).WithMany().HasForeignKey(x => x.EditorUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.OutlineApprovedByUser).WithMany().HasForeignKey(x => x.OutlineApprovedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StoryVersion>().WithMany().HasForeignKey(x => x.BaseVersionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StoryVersion>().WithMany().HasForeignKey(x => x.RestoredFromVersionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.StoryId, x.VersionNo }).IsUnique();
        builder.HasIndex(x => x.StoryId).IsUnique().HasFilter("\"IsCurrent\" = true AND \"IsDeleted\" = false");
        builder.HasAlternateKey(x => new { x.Id, x.StoryId });
        builder.HasQueryFilter(x => !x.IsDeleted);

        builder.OwnsOne(x => x.AuthoringMetadata, owned =>
        {
            owned.ToTable("story_version_authoring_metadata");
            owned.WithOwner().HasForeignKey(x => x.StoryVersionId);
            owned.HasKey(x => x.StoryVersionId);
            owned.Property(x => x.StoryVersionId).ValueGeneratedNever();
            owned.Property(x => x.AuthoringOrigin).HasMaxLength(20);
            owned.Property(x => x.AssistanceProvenanceJson).HasColumnType("jsonb");
            owned.Property(x => x.SourceDeclaration).HasMaxLength(2000).IsRequired();
            owned.Property(x => x.RightsDeclaration).HasMaxLength(2000).IsRequired();
            owned.Property(x => x.ImportFileName).HasMaxLength(255);
            owned.Property(x => x.ImportSourceHash).HasMaxLength(64);
            owned.Property(x => x.ImportPreviewHash).HasMaxLength(64);
            owned.HasOne<Story>().WithMany().HasForeignKey(x => x.StoryId).OnDelete(DeleteBehavior.Restrict);
            owned.HasIndex(x => new { x.StoryId, x.ImportPreviewHash }).IsUnique().HasFilter("\"ImportPreviewHash\" IS NOT NULL");
        });
        builder.Navigation(x => x.AuthoringMetadata).IsRequired();
        builder.OwnsOne(x => x.Moderation, owned =>
        {
            owned.ToTable("story_version_moderation_states");
            owned.WithOwner().HasForeignKey(x => x.StoryVersionId);
            owned.HasKey(x => x.StoryVersionId);
            owned.Property(x => x.StoryVersionId).ValueGeneratedNever();
            owned.Property(x => x.ModerationZone).HasConversion<string>().HasMaxLength(30);
            owned.Property(x => x.GrayZoneFlags).HasColumnType("jsonb");
            owned.Property(x => x.GrayZoneCleared).HasDefaultValue(false);
            owned.Property(x => x.ReviewStatus).HasConversion<string>().HasMaxLength(30).HasDefaultValue(VersionReviewStatus.None);
            owned.Property(x => x.ReviewFeedback).HasColumnType("jsonb");
            owned.HasOne(x => x.GrayZoneClearedByUser).WithMany().HasForeignKey(x => x.GrayZoneClearedByUserId).OnDelete(DeleteBehavior.Restrict);
            owned.HasOne(x => x.ReviewedByUser).WithMany().HasForeignKey(x => x.ReviewedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Navigation(x => x.Moderation).IsRequired();
        builder.OwnsOne(x => x.MediaState, owned =>
        {
            owned.ToTable("story_version_media_states");
            owned.WithOwner().HasForeignKey(x => x.StoryVersionId);
            owned.HasKey(x => x.StoryVersionId);
            owned.Property(x => x.StoryVersionId).ValueGeneratedNever();
            owned.Property(x => x.MediaPackageRevision).HasDefaultValue(0);
            owned.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.MediaConfirmedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Navigation(x => x.MediaState).IsRequired();
    }
}
