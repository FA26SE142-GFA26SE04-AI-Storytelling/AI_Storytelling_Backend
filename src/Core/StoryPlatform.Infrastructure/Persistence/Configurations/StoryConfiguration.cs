using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class StoryConfiguration : IEntityTypeConfiguration<Story>
{
    public void Configure(EntityTypeBuilder<Story> builder)
    {
        builder.ToTable("stories");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Title)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(s => s.Description)
            .HasMaxLength(1000);

        builder.Property(s => s.AgeBand)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(s => s.Genre)
            .HasMaxLength(50);

        builder.Property(s => s.MoralLesson)
            .HasMaxLength(500);

        builder.Property(s => s.Language)
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(s => s.CoverImageUrl)
            .HasMaxLength(500);

        builder.Property(s => s.Source)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(s => s.Status)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        // Foreign key to UserAccount (Author)
        builder.HasOne(s => s.Author)
            .WithMany(u => u.Stories)
            .HasForeignKey(s => s.AuthorUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Foreign key to ChildProfile (owner of the story)
        builder.HasOne(s => s.ChildProfile)
            .WithMany()
            .HasForeignKey(s => s.ChildProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        // Soft delete global filter
        builder.HasQueryFilter(s => !s.IsDeleted);
    }
}
