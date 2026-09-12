using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class SafetyPolicyConfiguration : IEntityTypeConfiguration<SafetyPolicy>
{
    public void Configure(EntityTypeBuilder<SafetyPolicy> builder)
    {
        builder.ToTable("safety_policies");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.RequiredApprovalMode)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.HasOne(x => x.ChildProfile)
            .WithMany()
            .HasForeignKey(x => x.ChildProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.ChildProfileId)
            .IsUnique();

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
