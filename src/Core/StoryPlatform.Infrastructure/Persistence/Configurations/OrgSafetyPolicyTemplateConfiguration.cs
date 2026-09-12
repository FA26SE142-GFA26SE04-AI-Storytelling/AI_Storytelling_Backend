using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class OrgSafetyPolicyTemplateConfiguration : IEntityTypeConfiguration<OrgSafetyPolicyTemplate>
{
    public void Configure(EntityTypeBuilder<OrgSafetyPolicyTemplate> builder)
    {
        builder.ToTable("org_safety_policy_templates");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.RequiredApprovalModeDefault)
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.HasOne(x => x.Organization)
            .WithMany()
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.OrganizationId)
            .IsUnique();

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
