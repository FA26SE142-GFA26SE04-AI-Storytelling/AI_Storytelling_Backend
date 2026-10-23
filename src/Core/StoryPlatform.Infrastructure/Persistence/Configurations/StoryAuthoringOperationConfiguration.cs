using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public sealed class StoryAuthoringOperationConfiguration : IEntityTypeConfiguration<StoryAuthoringOperation>
{
    public void Configure(EntityTypeBuilder<StoryAuthoringOperation> b)
    {
        b.ToTable("story_authoring_operations");
        b.HasKey(x => x.Id);
        b.Property(x => x.Kind).HasMaxLength(30).IsRequired();
        b.Property(x => x.OperationId).HasMaxLength(100).IsRequired();
        b.Property(x => x.RequestHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.OperationResponseJson).HasColumnType("text").IsRequired();
        b.HasIndex(x => new { x.StoryId, x.OperationId }).IsUnique();
        b.HasOne(x => x.StoryVersion).WithMany(x => x.AuthoringOperations)
            .HasForeignKey(x => new { x.StoryVersionId, x.StoryId })
            .HasPrincipalKey(x => new { x.Id, x.StoryId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasQueryFilter(x => !x.IsDeleted);
    }
}
