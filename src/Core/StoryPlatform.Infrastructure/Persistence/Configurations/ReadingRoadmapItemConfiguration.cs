using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class ReadingRoadmapItemConfiguration : IEntityTypeConfiguration<ReadingRoadmapItem>
{
    public void Configure(EntityTypeBuilder<ReadingRoadmapItem> builder)
    {
        builder.ToTable("reading_roadmap_items");

        builder.HasKey(x => x.Id);

        builder.HasOne(x => x.Story).WithMany().HasForeignKey(x => x.StoryId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.ReadingRoadmapId, x.Position }).IsUnique();
        builder.HasIndex(x => new { x.ReadingRoadmapId, x.StoryId }).IsUnique();

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
