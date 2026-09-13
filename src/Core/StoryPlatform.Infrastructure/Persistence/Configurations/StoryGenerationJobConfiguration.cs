using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class StoryGenerationJobConfiguration : IEntityTypeConfiguration<StoryGenerationJob>
{
    public void Configure(EntityTypeBuilder<StoryGenerationJob> builder)
    {
        builder.ToTable("story_generation_jobs");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Stage)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(x => x.Operation)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(x => x.ConcurrencyToken)
            .HasMaxLength(32)
            .IsRequired()
            .IsConcurrencyToken();

        builder.HasIndex(x => new { x.Status, x.Stage, x.LeaseExpiresAt });

        builder.Property(x => x.OperationKey).HasMaxLength(100);

        builder.Property(x => x.GuardrailResult)
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.Property(x => x.FallbackMessage)
            .HasColumnType("text");

        builder.Property(x => x.ErrorCode).HasMaxLength(100);
        builder.Property(x => x.GenerationMetadataJson).HasColumnType("jsonb");

        builder.HasOne(x => x.Story)
            .WithMany()
            .HasForeignKey(x => x.StoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.PromptCatalogVersion)
            .WithMany()
            .HasForeignKey(x => x.PromptCatalogVersionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.GenerationRequest)
            .WithMany()
            .HasForeignKey(x => x.GenerationRequestId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.StoryVersion)
            .WithMany()
            .HasForeignKey(x => x.StoryVersionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.BaseStoryVersion)
            .WithMany()
            .HasForeignKey(x => x.BaseStoryVersionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.RequestedByUser)
            .WithMany()
            .HasForeignKey(x => x.RequestedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.StoryId)
            .IsUnique()
            .HasFilter("\"Operation\" IN ('GenerateOutline', 'RegenerateOutline') AND \"Status\" IN ('Pending', 'Processing') AND \"IsDeleted\" = false");

        builder.HasIndex(x => new { x.Operation, x.StoryVersionId })
            .IsUnique()
            .HasFilter("\"StoryVersionId\" IS NOT NULL AND \"IsDeleted\" = false");

        builder.HasIndex(x => new { x.RequestedByUserId, x.Operation, x.OperationKey })
            .IsUnique()
            .HasFilter("\"RequestedByUserId\" IS NOT NULL AND \"OperationKey\" IS NOT NULL AND \"IsDeleted\" = false");

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
