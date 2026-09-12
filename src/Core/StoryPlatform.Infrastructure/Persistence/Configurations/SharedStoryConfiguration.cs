using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class SharedStoryConfiguration : IEntityTypeConfiguration<SharedStory>
{
    public void Configure(EntityTypeBuilder<SharedStory> builder)
    {
        builder.ToTable("shared_stories");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ShareMode)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(x => x.TeacherStatus)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.HasOne(x => x.Story)
            .WithMany()
            .HasForeignKey(x => x.StoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.SharedByUser)
            .WithMany()
            .HasForeignKey(x => x.SharedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ClassGroup)
            .WithMany()
            .HasForeignKey(x => x.ClassGroupId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ReviewedByUser)
            .WithMany()
            .HasForeignKey(x => x.ReviewedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.StoryId, x.ClassGroupId })
            .IsUnique();

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
