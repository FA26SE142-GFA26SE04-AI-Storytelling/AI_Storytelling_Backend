using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class StoryWishConfiguration : IEntityTypeConfiguration<StoryWish>
{
    public void Configure(EntityTypeBuilder<StoryWish> builder)
    {
        builder.ToTable("story_wishes");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.HasOne(x => x.ChildProfile).WithMany().HasForeignKey(x => x.ChildProfileId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.CharacterCard).WithMany().HasForeignKey(x => x.CharacterCardId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.PlaceCard).WithMany().HasForeignKey(x => x.PlaceCardId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ChallengeCard).WithMany().HasForeignKey(x => x.ChallengeCardId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.LessonCard).WithMany().HasForeignKey(x => x.LessonCardId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.FulfilledStory).WithMany().HasForeignKey(x => x.FulfilledStoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.ChildProfileId)
            .IsUnique()
            .HasFilter("\"Status\" = 'Draft' AND \"IsDeleted\" = false")
            .HasDatabaseName("IX_story_wishes_one_draft_per_child");
        builder.HasIndex(x => new { x.ChildProfileId, x.Status, x.SubmittedAt })
            .HasDatabaseName("IX_story_wishes_child_status_submitted");

        builder.HasOne(x => x.GeneratedStory)
            .WithMany()
            .HasForeignKey(x => x.GeneratedStoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(x => x.DeclineReason).HasMaxLength(500);
        builder.Property(x => x.ResolutionNote).HasMaxLength(50);

        builder.HasIndex(x => new { x.Status, x.PendingSince })
            .HasDatabaseName("IX_story_wishes_status_pending_since");

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
