using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class ClassGroupMemberConfiguration : IEntityTypeConfiguration<ClassGroupMember>
{
    public void Configure(EntityTypeBuilder<ClassGroupMember> builder)
    {
        builder.ToTable("class_group_members");

        builder.HasKey(x => x.Id);

        builder.HasOne(x => x.ClassGroup)
            .WithMany()
            .HasForeignKey(x => x.ClassGroupId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ChildProfile)
            .WithMany()
            .HasForeignKey(x => x.ChildProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.ClassGroupId, x.ChildProfileId })
            .IsUnique();

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
