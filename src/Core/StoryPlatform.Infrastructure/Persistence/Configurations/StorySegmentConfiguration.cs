using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public sealed class StorySegmentConfiguration : IEntityTypeConfiguration<StorySegment>
{
    public void Configure(EntityTypeBuilder<StorySegment> builder)
    {
        builder.ToTable("story_segments", table =>
        {
            table.HasCheckConstraint("CK_story_segments_segment_order", "\"SegmentOrder\" >= 1");
            table.HasCheckConstraint("CK_story_segments_offsets", "\"StartOffset\" >= 0 AND \"EndOffset\" > \"StartOffset\"");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TextContent).HasColumnType("text").IsRequired();
        builder.HasIndex(x => new { x.StorySceneId, x.SegmentOrder }).IsUnique();
        builder.HasOne(x => x.StoryScene).WithMany().HasForeignKey(x => x.StorySceneId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
