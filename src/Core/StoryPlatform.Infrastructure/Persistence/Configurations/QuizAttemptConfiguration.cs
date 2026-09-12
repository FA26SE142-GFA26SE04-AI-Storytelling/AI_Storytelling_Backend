using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class QuizAttemptConfiguration : IEntityTypeConfiguration<QuizAttempt>
{
    public void Configure(EntityTypeBuilder<QuizAttempt> builder)
    {
        builder.ToTable("quiz_attempts");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.AnswerGiven)
            .HasColumnType("text");

        builder.HasOne(x => x.ReadingSession)
            .WithMany()
            .HasForeignKey(x => x.ReadingSessionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.QuizItem)
            .WithMany()
            .HasForeignKey(x => x.QuizItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
