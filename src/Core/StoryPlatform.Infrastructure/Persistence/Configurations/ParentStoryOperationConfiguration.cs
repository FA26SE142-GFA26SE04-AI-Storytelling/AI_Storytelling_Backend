using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public sealed class ParentStoryOperationConfiguration : IEntityTypeConfiguration<ParentStoryOperation>
{
    public void Configure(EntityTypeBuilder<ParentStoryOperation> b)
    {
        b.ToTable("parent_story_operations");
        b.HasKey(x => x.Id);
        b.Property(x => x.Kind).HasMaxLength(40).IsRequired();
        b.Property(x => x.OperationId).HasMaxLength(100).IsRequired();
        b.Property(x => x.RequestHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.ContextFingerprint).HasMaxLength(64).IsRequired();
        b.Property(x => x.ResultJson).HasColumnType("text").IsRequired();
        b.HasIndex(x => new { x.StoryId, x.ActorUserId, x.OperationId }).IsUnique();
        b.HasIndex(x => new { x.StoryId, x.Kind });
        b.HasIndex(x => new { x.ChildProfileId, x.Kind });
        b.HasOne<ChildProfile>().WithMany().HasForeignKey(x => x.ChildProfileId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Story>().WithMany().HasForeignKey(x => x.StoryId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<StoryVersion>().WithMany().HasForeignKey(x => x.StoryVersionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasQueryFilter(x => !x.IsDeleted);
    }
}
