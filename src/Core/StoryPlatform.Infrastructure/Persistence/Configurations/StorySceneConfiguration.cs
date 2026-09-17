using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public sealed class StorySceneConfiguration : IEntityTypeConfiguration<StoryScene>
{
    public void Configure(EntityTypeBuilder<StoryScene> builder)
    {
        builder.ToTable("story_scenes", table =>
        {
            table.HasCheckConstraint("CK_story_scenes_scene_index", "\"SceneIndex\" >= 0");
            table.HasCheckConstraint("CK_story_scenes_text_range", "\"TextRangeStart\" >= 0 AND \"TextRangeEnd\" > \"TextRangeStart\"");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.SceneText).HasColumnType("text").IsRequired();
        builder.Property(x => x.VisualDescription).HasColumnType("text");
        builder.HasIndex(x => new { x.StoryVersionId, x.SceneIndex }).IsUnique();
        builder.HasOne(x => x.StoryVersion).WithMany().HasForeignKey(x => x.StoryVersionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
