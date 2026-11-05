using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public sealed class ParentStoryPackageConfiguration : IEntityTypeConfiguration<ParentStoryPackage>
{
    public void Configure(EntityTypeBuilder<ParentStoryPackage> b)
    {
        b.ToTable("parent_story_packages");
        b.HasKey(x => x.Id);
        b.Property(x => x.ConcurrencyToken).HasMaxLength(32).IsConcurrencyToken();
        b.Property(x => x.Status).HasMaxLength(30).IsRequired();
        b.Property(x => x.LastErrorCode).HasMaxLength(100);
        b.Property(x => x.SourceFingerprint).HasMaxLength(64).IsRequired();
        b.Property(x => x.ContextFingerprint).HasMaxLength(64).IsRequired();
        foreach (var name in new[] { nameof(ParentStoryPackage.ContextSnapshotJson), nameof(ParentStoryPackage.ComponentsJson),
                     nameof(ParentStoryPackage.GrayZoneAssessmentJson), nameof(ParentStoryPackage.CheckResultJson), nameof(ParentStoryPackage.ManifestJson) })
            b.Property<string>(name).HasColumnType("jsonb").IsRequired();
        b.HasOne<Story>().WithMany().HasForeignKey(x => x.StoryId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<StoryVersion>().WithMany().HasForeignKey(x => x.StoryVersionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ChildProfile>().WithMany().HasForeignKey(x => x.ChildProfileId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.ConfirmedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ParentStoryPackage>().WithMany().HasForeignKey(x => x.PreviousPackageId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.StoryVersionId, x.ChildProfileId, x.Revision }).IsUnique();
        b.HasIndex(x => new { x.StoryId, x.Status });
        b.HasQueryFilter(x => !x.IsDeleted);
    }
}
