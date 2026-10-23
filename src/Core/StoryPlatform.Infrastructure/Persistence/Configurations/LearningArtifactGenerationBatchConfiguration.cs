using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public sealed class LearningArtifactGenerationBatchConfiguration : IEntityTypeConfiguration<LearningArtifactGenerationBatch>
{
    public void Configure(EntityTypeBuilder<LearningArtifactGenerationBatch> b)
    {
        b.ToTable("learning_artifact_generation_batches");
        b.HasKey(x => x.Id);
        b.Property(x => x.Kind).HasMaxLength(40).IsRequired();
        b.Property(x => x.SourceFingerprint).HasMaxLength(64).IsRequired();
        b.Property(x => x.ContextFingerprint).HasMaxLength(64).IsRequired();
        b.Property(x => x.RootClaimToken).HasMaxLength(32);
        b.Property(x => x.ConcurrencyToken).HasMaxLength(32).IsConcurrencyToken().IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.ErrorCode).HasMaxLength(100);
        b.Property(x => x.RequestJson).HasColumnType("jsonb");
        b.Property(x => x.ResultJson).HasColumnType("jsonb");
        b.Property(x => x.ShortfallReason).HasMaxLength(300);
        b.HasOne<Story>().WithMany().HasForeignKey(x => x.StoryId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<StoryVersion>().WithMany().HasForeignKey(x => x.StoryVersionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<StoryGenerationJob>().WithMany().HasForeignKey(x => x.RootJobId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.RequestedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.RootJobId, x.Kind, x.BatchNumber }).IsUnique();
        b.HasIndex(x => new { x.StoryId, x.Status, x.LeaseExpiresAt });
        b.HasQueryFilter(x => !x.IsDeleted);
    }
}
