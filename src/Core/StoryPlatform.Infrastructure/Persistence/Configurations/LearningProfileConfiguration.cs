using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class LearningProfileConfiguration : IEntityTypeConfiguration<LearningProfile>
{
    public void Configure(EntityTypeBuilder<LearningProfile> builder)
    {
        builder.ToTable("learning_profiles");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ComprehensionGoal)
            .HasMaxLength(500);

        builder.HasOne(x => x.ChildProfile)
            .WithMany()
            .HasForeignKey(x => x.ChildProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.ChildProfileId)
            .IsUnique();

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
