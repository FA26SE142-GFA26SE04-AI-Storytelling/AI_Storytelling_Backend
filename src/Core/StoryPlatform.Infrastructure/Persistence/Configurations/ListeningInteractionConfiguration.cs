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

        // Mỗi đoạn neo tối đa 1 tương tác, nên mỗi scene tối đa 1 tương tác.
        builder.HasIndex(x => new { x.StoryVersionId, x.AnchorOffset })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false");

        builder.HasIndex(x => new { x.StoryVersionId, x.Type });

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
