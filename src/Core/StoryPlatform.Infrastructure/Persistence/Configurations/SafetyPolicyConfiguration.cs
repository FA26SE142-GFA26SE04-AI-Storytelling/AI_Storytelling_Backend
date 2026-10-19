using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class SafetyPolicyConfiguration : IEntityTypeConfiguration<SafetyPolicy>
{
    public void Configure(EntityTypeBuilder<SafetyPolicy> builder)
    {
        builder.ToTable("safety_policies");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.RequiredApprovalMode)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(x => x.SafetyScoreThreshold)
            .HasColumnType("decimal(5,2)");

        builder.Property(x => x.ReadabilityScoreThreshold)
            .HasColumnType("decimal(5,2)");

        builder.Property(x => x.ComprehensionThresholdPercent)
            .HasColumnType("decimal(5,2)")
            .HasDefaultValue(70m);

        builder.Property(x => x.StoryWishesEnabled).HasDefaultValue(true);
        builder.Property(x => x.AllowInteractiveStories).HasDefaultValue(true);
        builder.Property(x => x.ChildCreationEnabled).HasDefaultValue(true);
        builder.Property(x => x.ChildCreationMode)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired()
            .HasDefaultValue(ChildCreationMode.ReviewFirst)
            .HasSentinel(ChildCreationMode.ReviewFirst);
        builder.Property(x => x.VoiceAnswersEnabled).HasDefaultValue(false);

        builder.HasOne(x => x.ChildProfile)
            .WithMany()
            .HasForeignKey(x => x.ChildProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ConsentedByUser)
            .WithMany()
            .HasForeignKey(x => x.ConsentedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.ChildProfileId)
            .IsUnique();

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
