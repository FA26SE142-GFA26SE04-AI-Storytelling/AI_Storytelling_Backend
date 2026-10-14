using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class ReadingRoadmapConfiguration : IEntityTypeConfiguration<ReadingRoadmap>
{
    public void Configure(EntityTypeBuilder<ReadingRoadmap> builder)
    {
        builder.ToTable("reading_roadmaps");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasColumnType("text");
        builder.Property(x => x.AgeBand).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(30).IsRequired()
            .HasDefaultValue(RoadmapTemplateStatus.Draft);
        builder.Property(x => x.ArchivedReason).HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.ArchiveNote).HasMaxLength(500);

        builder.HasOne(x => x.MainCategory).WithMany().HasForeignKey(x => x.MainCategoryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.PublishedByUser).WithMany().HasForeignKey(x => x.PublishedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ArchivedByUser).WithMany().HasForeignKey(x => x.ArchivedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ClonedFrom).WithMany().HasForeignKey(x => x.ClonedFromId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Items).WithOne(x => x.ReadingRoadmap)
            .HasForeignKey(x => x.ReadingRoadmapId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.AgeBand, x.MainCategoryId })
            .IsUnique()
            .HasFilter("\"Status\" = 'Published' AND NOT \"IsDeleted\"");

        builder.HasIndex(x => x.ClonedFromId)
            .IsUnique()
            .HasFilter("\"Status\" IN ('Draft', 'PendingReview') AND NOT \"IsDeleted\"");

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
