using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class ListeningInteractionConfiguration : IEntityTypeConfiguration<ListeningInteraction>
{
    public void Configure(EntityTypeBuilder<ListeningInteraction> builder)
    {
        builder.ToTable("listening_interactions");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Type)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(x => x.Kind)
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.Property(x => x.Prompt).HasColumnType("text");
        builder.Property(x => x.TargetLabel).HasColumnType("text");
        builder.Property(x => x.HotspotJson).HasColumnType("text");
        builder.Property(x => x.AcceptedOrdersJson).HasColumnType("text");
        builder.Property(x => x.RelationsJson).HasColumnType("text");
        builder.Property(x => x.SourceQuote).HasColumnType("text");
        builder.Property(x => x.CharacterName).HasMaxLength(100);
        builder.Property(x => x.HiddenWord).HasMaxLength(100);
        builder.Property(x => x.Hint).HasColumnType("text");
        builder.Property(x => x.Explanation).HasColumnType("text");
        builder.Property(x => x.MatchFeedback).HasColumnType("text");
        builder.Property(x => x.NoMatchFeedback).HasColumnType("text");
        builder.Property(x => x.WaitFeedback).HasColumnType("text");

        builder.HasOne(x => x.StoryVersion)
            .WithMany()
            .HasForeignKey(x => x.StoryVersionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.StoryScene)
            .WithMany()
            .HasForeignKey(x => x.StorySceneId)
            .OnDelete(DeleteBehavior.Restrict);

        // ReadAloud đứng chung neo cuối với EventOrdering và được trả riêng cho bé.
        builder.HasIndex(x => new { x.StoryVersionId, x.TargetAge, x.AnchorOffset })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false AND \"ParentStoryPackageId\" IS NULL AND \"Type\" <> 'ReadAloud'");
        builder.HasIndex(x => new { x.ParentStoryPackageId, x.TargetAge, x.AnchorOffset }).IsUnique()
            .HasFilter("\"IsDeleted\" = false AND \"ParentStoryPackageId\" IS NOT NULL AND \"Type\" <> 'ReadAloud'");

        builder.HasIndex(x => new { x.StoryVersionId, x.Type });
        builder.HasIndex(x => new { x.StoryVersionId, x.ParentStoryPackageId, x.TargetAge });

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
