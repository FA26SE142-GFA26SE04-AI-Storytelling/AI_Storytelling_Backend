using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class SharedStoryRecipientConfiguration : IEntityTypeConfiguration<SharedStoryRecipient>
{
    public void Configure(EntityTypeBuilder<SharedStoryRecipient> builder)
    {
        builder.ToTable("shared_story_recipients");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.HasOne(x => x.SharedStory)
            .WithMany()
            .HasForeignKey(x => x.SharedStoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.RecipientUser)
            .WithMany()
            .HasForeignKey(x => x.RecipientUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
