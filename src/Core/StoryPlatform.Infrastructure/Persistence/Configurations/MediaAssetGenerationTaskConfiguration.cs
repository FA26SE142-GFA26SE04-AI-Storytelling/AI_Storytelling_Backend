using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public sealed class MediaAssetGenerationTaskConfiguration : IEntityTypeConfiguration<MediaAssetGenerationTask>
{
    public void Configure(EntityTypeBuilder<MediaAssetGenerationTask> builder)
    {
        builder.ToTable("media_asset_generation_tasks");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(x => x.ConcurrencyToken).HasMaxLength(32).IsRequired().IsConcurrencyToken();
        builder.Property(x => x.ErrorCode).HasMaxLength(100);
        builder.HasOne<Story>().WithMany().HasForeignKey(x => x.StoryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StoryVersion>().WithMany().HasForeignKey(x => x.StoryVersionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StoryGenerationJob>().WithMany().HasForeignKey(x => x.BranchJobId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MediaAsset>().WithMany().HasForeignKey(x => x.MediaAssetId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<IllustrationBeat>().WithMany().HasForeignKey(x => x.IllustrationBeatId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MediaContext>().WithMany().HasForeignKey(x => x.MediaContextId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.MediaAssetId).IsUnique();
        builder.HasIndex(x => new { x.BranchJobId, x.PackageRevision, x.Status, x.NextAttemptAt });
        builder.HasIndex(x => new { x.StoryId, x.Status, x.LeaseExpiresAt });
        builder.HasIndex(x => new { x.Status, x.NextAttemptAt, x.QueuedAt });
        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
