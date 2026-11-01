using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class ListeningInteractionAttemptConfiguration : IEntityTypeConfiguration<ListeningInteractionAttempt>
{
    public void Configure(EntityTypeBuilder<ListeningInteractionAttempt> builder)
    {
        builder.ToTable("listening_interaction_attempts");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.AnswerText).HasMaxLength(200);
        builder.Property(x => x.OrderedOptionIdsJson).HasMaxLength(200);

        builder.HasOne(x => x.ReadingSession)
            .WithMany()
            .HasForeignKey(x => x.ReadingSessionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ChildProfile)
            .WithMany()
            .HasForeignKey(x => x.ChildProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ListeningInteraction)
            .WithMany()
            .HasForeignKey(x => x.ListeningInteractionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.SelectedOption)
            .WithMany()
            .HasForeignKey(x => x.SelectedOptionId)
            .OnDelete(DeleteBehavior.Restrict);

        // EventOrdering thử nhiều lần trong một phiên nên không unique; loại khác kiểm tra bằng code.
        builder.HasIndex(x => new { x.ReadingSessionId, x.ListeningInteractionId });

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
