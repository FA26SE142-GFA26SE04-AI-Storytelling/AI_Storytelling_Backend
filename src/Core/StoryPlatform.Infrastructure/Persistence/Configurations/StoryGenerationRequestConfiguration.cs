using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public sealed class StoryGenerationRequestConfiguration : IEntityTypeConfiguration<StoryGenerationRequest>
{
    public void Configure(EntityTypeBuilder<StoryGenerationRequest> builder)
    {
        builder.ToTable("story_generation_requests");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.IdempotencyKey).IsRequired().HasMaxLength(100);
        builder.Property(x => x.InputFingerprint).IsRequired().HasMaxLength(64);
        builder.Property(x => x.ContextFingerprint).IsRequired().HasMaxLength(64);
        builder.Property(x => x.ContextSnapshotJson).IsRequired().HasColumnType("jsonb");
        builder.Property(x => x.AcceptedInputJson).HasColumnType("jsonb");
        builder.Property(x => x.Status).HasConversion<string>().IsRequired().HasMaxLength(30);
        builder.Property(x => x.ConcurrencyToken).IsRequired().HasMaxLength(32).IsConcurrencyToken();
        builder.Property(x => x.LastRetryKey).HasMaxLength(100);
        builder.Property(x => x.GuardrailDecision).HasMaxLength(30);
        builder.Property(x => x.ReasonCode).HasMaxLength(100);
        builder.Property(x => x.FallbackMessage).HasMaxLength(500);
        builder.Property(x => x.GuardrailCheckVersion).HasMaxLength(100);

        builder.HasOne(x => x.Story)
            .WithMany()
            .HasForeignKey(x => x.StoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.SubmittedByUser)
            .WithMany()
            .HasForeignKey(x => x.SubmittedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.HandoffJob)
            .WithMany()
            .HasForeignKey(x => x.HandoffJobId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.SubmittedByUserId, x.IdempotencyKey }).IsUnique();
        builder.HasIndex(x => x.HandoffJobId).IsUnique();
        builder.HasIndex(x => x.StoryId)
            .IsUnique()
            .HasFilter($"\"Status\" IN ('{GenerationInputStatus.PendingInput}', '{GenerationInputStatus.CheckingInput}', '{GenerationInputStatus.InputAccepted}') AND \"IsDeleted\" = false");

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
