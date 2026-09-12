using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class LearningProfileTopicConfiguration : IEntityTypeConfiguration<LearningProfileTopic>
{
    public void Configure(EntityTypeBuilder<LearningProfileTopic> builder)
    {
        builder.ToTable("learning_profile_topics");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Topic)
            .IsRequired()
            .HasMaxLength(150);

        builder.Property(x => x.Relation)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.HasOne(x => x.LearningProfile)
            .WithMany()
            .HasForeignKey(x => x.LearningProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
