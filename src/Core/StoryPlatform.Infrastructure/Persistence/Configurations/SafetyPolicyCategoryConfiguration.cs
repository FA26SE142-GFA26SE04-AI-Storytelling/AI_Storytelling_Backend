using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class SafetyPolicyCategoryConfiguration : IEntityTypeConfiguration<SafetyPolicyCategory>
{
    public void Configure(EntityTypeBuilder<SafetyPolicyCategory> builder)
    {
        builder.ToTable("safety_policy_categories");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Rule)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.HasOne(x => x.SafetyPolicy)
            .WithMany()
            .HasForeignKey(x => x.SafetyPolicyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ContentCategory)
            .WithMany()
            .HasForeignKey(x => x.ContentCategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.SafetyPolicyId, x.ContentCategoryId })
            .IsUnique();

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
