using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class ChildProfileVersionHistoryConfiguration : IEntityTypeConfiguration<ChildProfileVersionHistory>
{
    public void Configure(EntityTypeBuilder<ChildProfileVersionHistory> builder)
    {
        builder.ToTable("child_profile_version_history");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.PreviousConfig)
            .IsRequired()
            .HasColumnType("text");

        builder.Property(x => x.NewConfig)
            .IsRequired()
            .HasColumnType("text");

        builder.Property(x => x.VersionStatus)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(x => x.Source)
            .HasConversion<string>()
            .HasMaxLength(30)
            .HasDefaultValue(VersionHistorySource.Recommendation)
            .HasSentinel(VersionHistorySource.Recommendation)
            .IsRequired();

        builder.HasOne(x => x.ChildProfile)
            .WithMany()
            .HasForeignKey(x => x.ChildProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Recommendation)
            .WithMany()
            .HasForeignKey(x => x.RecommendationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.AppliedByUser)
            .WithMany()
            .HasForeignKey(x => x.AppliedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
