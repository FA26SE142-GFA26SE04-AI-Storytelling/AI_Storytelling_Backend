using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public sealed class IllustrationBeatConfiguration : IEntityTypeConfiguration<IllustrationBeat>
{
    public void Configure(EntityTypeBuilder<IllustrationBeat> builder)
    {
        builder.ToTable("illustration_beats", table =>
        {
            table.HasCheckConstraint("CK_illustration_beats_order", "\"BeatOrder\" BETWEEN 1 AND 3");
            table.HasCheckConstraint("CK_illustration_beats_offsets", "\"StartOffset\" >= 0 AND \"EndOffset\" > \"StartOffset\"");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.VisualFocus).HasMaxLength(500).IsRequired();
        builder.HasOne(x => x.StoryScene).WithMany().HasForeignKey(x => x.StorySceneId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.StorySceneId, x.BeatOrder }).IsUnique();
        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
