using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class LearningInsightConfiguration : IEntityTypeConfiguration<LearningInsight>
{
    public void Configure(EntityTypeBuilder<LearningInsight> builder)
    {
        builder.ToTable("learning_insights");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(x => x.Observation)
            .IsRequired()
            .HasColumnType("text");

        builder.Property(x => x.Evidence)
            .IsRequired()
            .HasColumnType("text");

        builder.HasOne(x => x.ChildProfile)
            .WithMany()
            .HasForeignKey(x => x.ChildProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
