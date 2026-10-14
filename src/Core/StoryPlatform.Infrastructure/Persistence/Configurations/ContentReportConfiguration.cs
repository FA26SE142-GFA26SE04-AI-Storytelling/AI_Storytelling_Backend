using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class ContentReportConfiguration : IEntityTypeConfiguration<ContentReport>
{
    public void Configure(EntityTypeBuilder<ContentReport> builder)
    {
        builder.ToTable("content_reports");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Reason)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(x => x.Description)
            .HasColumnType("text");

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(x => x.AttachmentUrl).HasMaxLength(500);
        builder.Property(x => x.IsReviewItem).HasDefaultValue(false);

        builder.HasOne(x => x.Story)
            .WithMany()
            .HasForeignKey(x => x.StoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ReporterUser)
            .WithMany()
            .HasForeignKey(x => x.ReporterUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ReviewedByUser)
            .WithMany()
            .HasForeignKey(x => x.ReviewedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.StoryVersion)
            .WithMany()
            .HasForeignKey(x => x.StoryVersionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ReadingRoadmap)
            .WithMany()
            .HasForeignKey(x => x.ReadingRoadmapId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
