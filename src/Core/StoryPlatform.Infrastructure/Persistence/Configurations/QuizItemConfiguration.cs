using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class QuizItemConfiguration : IEntityTypeConfiguration<QuizItem>
{
    public void Configure(EntityTypeBuilder<QuizItem> builder)
    {
        builder.ToTable("quiz_items");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Type)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(x => x.Question)
            .IsRequired()
            .HasColumnType("text");

        builder.Property(x => x.CorrectAnswer).HasColumnType("text");
        builder.Property(x => x.Choices).HasColumnType("text");

        builder.HasOne(x => x.StoryVersion)
            .WithMany()
            .HasForeignKey(x => x.StoryVersionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
