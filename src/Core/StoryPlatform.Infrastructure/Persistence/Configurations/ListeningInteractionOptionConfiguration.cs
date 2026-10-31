using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class ListeningInteractionOptionConfiguration : IEntityTypeConfiguration<ListeningInteractionOption>
{
    public void Configure(EntityTypeBuilder<ListeningInteractionOption> builder)
    {
        builder.ToTable("listening_interaction_options");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Text)
            .IsRequired()
            .HasColumnType("text");

        builder.Property(x => x.Feedback).HasColumnType("text");
        builder.Property(x => x.SourceQuote).HasColumnType("text");
        builder.Property(x => x.ImageDescription).HasColumnType("text");

        builder.HasOne(x => x.ListeningInteraction)
            .WithMany()
            .HasForeignKey(x => x.ListeningInteractionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.ListeningInteractionId, x.SortOrder })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false");

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
