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

        builder.Property(x => x.EditType)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(x => x.Title)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.OutlineOpening).HasColumnType("text");
        builder.Property(x => x.OutlineDevelopment).HasColumnType("text");
        builder.Property(x => x.OutlineEnding).HasColumnType("text");
        builder.Property(x => x.Content).HasColumnType("text");
        builder.Property(x => x.AuthoringOrigin).HasMaxLength(20);
        builder.Property(x => x.AssistanceProvenanceJson).HasColumnType("jsonb");
        builder.Property(x => x.SourceDeclaration).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.RightsDeclaration).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.ImportFileName).HasMaxLength(255);
        builder.Property(x => x.ImportSourceHash).HasMaxLength(64);
        builder.Property(x => x.ImportPreviewHash).HasMaxLength(64);
        builder.Property(x => x.OperationId).HasMaxLength(100);
        builder.Property(x => x.RequestHash).HasMaxLength(64);
        builder.Property(x => x.OperationResponseJson).HasColumnType("text");
        builder.HasOne<StoryVersion>().WithMany().HasForeignKey(x => x.BaseVersionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StoryVersion>().WithMany().HasForeignKey(x => x.RestoredFromVersionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.StoryId, x.OperationId }).IsUnique().HasFilter("\"OperationId\" IS NOT NULL");
        builder.HasIndex(x => new { x.StoryId, x.ImportPreviewHash }).IsUnique().HasFilter("\"ImportPreviewHash\" IS NOT NULL");
        builder.Property(x => x.Lesson).HasColumnType("text");

        builder.Property(x => x.ReadabilityFkgl).HasColumnType("decimal(6,3)");
        builder.Property(x => x.ReadabilityFre).HasColumnType("decimal(6,3)");
        builder.Property(x => x.SafetyScore).HasColumnType("decimal(6,3)");

        builder.Property(x => x.ModerationZone).HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.GrayZoneFlags).HasColumnType("jsonb");
        builder.Property(x => x.GrayZoneCleared).HasDefaultValue(false);
        builder.Property(x => x.ReviewStatus)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired()
            .HasDefaultValue(VersionReviewStatus.None);
        builder.Property(x => x.ReviewFeedback).HasColumnType("jsonb");

        builder.HasOne(x => x.Story)
            .WithMany()
            .HasForeignKey(x => x.StoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.EditorUser)
            .WithMany()
            .HasForeignKey(x => x.EditorUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.OutlineApprovedByUser)
            .WithMany()
            .HasForeignKey(x => x.OutlineApprovedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.GrayZoneClearedByUser)
            .WithMany()
            .HasForeignKey(x => x.GrayZoneClearedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ReviewedByUser)
            .WithMany()
            .HasForeignKey(x => x.ReviewedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.StoryId, x.VersionNo }).IsUnique();
        builder.HasIndex(x => x.StoryId)
            .IsUnique()
            .HasFilter("\"IsCurrent\" = true AND \"IsDeleted\" = false");

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
