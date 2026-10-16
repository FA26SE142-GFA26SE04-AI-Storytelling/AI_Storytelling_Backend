using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public sealed class StoryAssistanceTaskConfiguration : IEntityTypeConfiguration<StoryAssistanceTask>
{
    public void Configure(EntityTypeBuilder<StoryAssistanceTask> b)
    {
        b.ToTable("story_assistance_tasks"); b.HasKey(x => x.Id);
        b.Property(x => x.CreateOperationId).HasMaxLength(100).IsRequired();
        b.Property(x => x.CreateRequestHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.CreateResponseJson).HasColumnType("text").IsRequired();
        b.Property(x => x.ContextJson).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.SnapshotHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.SourceRevisionToken).HasMaxLength(32).IsRequired();
        b.Property(x => x.EntryPoint).HasMaxLength(30).IsRequired();
        b.Property(x => x.Status).HasMaxLength(30).IsRequired();
        b.Property(x => x.ActiveOperationId).HasMaxLength(100);
        b.Property(x => x.FindingId).HasMaxLength(50);
        b.Property(x => x.Language).HasMaxLength(10).IsRequired();
        b.Property(x => x.AgeBand).HasMaxLength(20).IsRequired();
        b.HasIndex(x => new { x.StoryId, x.OwnerUserId, x.CreateOperationId }).IsUnique();
        b.HasIndex(x => new { x.OwnerUserId, x.StoryId, x.CreatedAt });
        b.HasOne<Story>().WithMany().HasForeignKey(x => x.StoryId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<StoryVersion>().WithMany().HasForeignKey(x => x.SourceVersionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<StoryAssistanceTask>().WithMany().HasForeignKey(x => x.ParentTaskId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<StoryAssistanceStep>().WithMany().HasForeignKey(x => x.SourceReviewStepId).OnDelete(DeleteBehavior.Restrict);
        b.HasQueryFilter(x => !x.IsDeleted);
    }
}
public sealed class StoryAssistanceStepConfiguration : IEntityTypeConfiguration<StoryAssistanceStep>
{
    public void Configure(EntityTypeBuilder<StoryAssistanceStep> b)
    {
        b.ToTable("story_assistance_steps"); b.HasKey(x => x.Id);
        b.Property(x => x.OperationId).HasMaxLength(100).IsRequired();
        b.Property(x => x.RequestHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.Kind).HasMaxLength(30).IsRequired();
        b.Property(x => x.Status).HasMaxLength(30).IsRequired();
        b.Property(x => x.InputJson).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.OutputJson).HasColumnType("jsonb");
        b.Property(x => x.ResponseJson).HasColumnType("text");
        b.Property(x => x.Provider).HasMaxLength(100); b.Property(x => x.Model).HasMaxLength(200);
        b.Property(x => x.PromptVersion).HasMaxLength(100); b.Property(x => x.ErrorCode).HasMaxLength(100);
        b.HasIndex(x => new { x.TaskId, x.OperationId }).IsUnique();
        b.HasIndex(x => new { x.TaskId, x.Sequence }).IsUnique();
        b.HasOne<StoryAssistanceTask>().WithMany().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<StoryAssistanceStep>().WithMany().HasForeignKey(x => x.DirectionStepId).OnDelete(DeleteBehavior.Restrict);
        b.HasQueryFilter(x => !x.IsDeleted);
    }
}
