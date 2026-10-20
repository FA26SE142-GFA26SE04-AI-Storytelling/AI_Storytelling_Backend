using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class ChildRoadmapItemConfiguration : IEntityTypeConfiguration<ChildRoadmapItem>
{
    public void Configure(EntityTypeBuilder<ChildRoadmapItem> builder)
    {
        builder.ToTable("child_roadmap_items");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(30).IsRequired()
            .HasDefaultValue(ChildRoadmapItemStatus.Waiting)
            .HasSentinel(ChildRoadmapItemStatus.Waiting);
        builder.Property(x => x.SkipReason).HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.Source).HasConversion<string>().HasMaxLength(30).IsRequired()
            .HasDefaultValue(ChildRoadmapItemSource.Manual)
            .HasSentinel(ChildRoadmapItemSource.Manual);
        builder.Property(x => x.Note).HasMaxLength(500);

        builder.HasOne(x => x.ChildProfile).WithMany().HasForeignKey(x => x.ChildProfileId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Story).WithMany().HasForeignKey(x => x.StoryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.SourceReadingRoadmap).WithMany().HasForeignKey(x => x.SourceReadingRoadmapId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.AddedByUser).WithMany().HasForeignKey(x => x.AddedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.RemovedByUser).WithMany().HasForeignKey(x => x.RemovedByUserId).OnDelete(DeleteBehavior.Restrict);

        // BR-L4A-06: mỗi lúc tối đa một truyện đang mở cho mỗi trẻ.
        builder.HasIndex(x => x.ChildProfileId)
            .IsUnique()
            .HasFilter("\"Status\" = 'Open' AND NOT \"IsDeleted\"");

        // BR-L4A-11: không thêm trùng một truyện chưa đọc xong.
        builder.HasIndex(x => new { x.ChildProfileId, x.StoryId })
            .IsUnique()
            .HasFilter("\"Status\" IN ('Waiting', 'Open') AND NOT \"IsDeleted\"");

        builder.HasIndex(x => new { x.ChildProfileId, x.Status, x.Position });
        builder.HasIndex(x => x.StoryId);

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
