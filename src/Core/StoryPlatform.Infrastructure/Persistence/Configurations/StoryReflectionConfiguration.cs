using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class StoryReflectionConfiguration : IEntityTypeConfiguration<StoryReflection>
{
    public void Configure(EntityTypeBuilder<StoryReflection> builder)
    {
        builder.ToTable("story_reflections");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Emotions).HasColumnType("text[]").IsRequired();
        builder.Property(x => x.NoteText).HasMaxLength(200);
        builder.Property(x => x.NoteAudioStoragePath).HasMaxLength(500);
        builder.Property(x => x.NoteAudioContentType).HasMaxLength(50);

        builder.HasOne(x => x.ReadingSession).WithMany().HasForeignKey(x => x.ReadingSessionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.FavoriteScene).WithMany().HasForeignKey(x => x.FavoriteSceneId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.FavoriteMediaAsset).WithMany().HasForeignKey(x => x.FavoriteMediaAssetId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.ReadingSessionId)
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false")
            .HasDatabaseName("UX_story_reflections_one_per_session");
        builder.HasIndex(x => x.ChildProfileId).HasDatabaseName("IX_story_reflections_child");

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
