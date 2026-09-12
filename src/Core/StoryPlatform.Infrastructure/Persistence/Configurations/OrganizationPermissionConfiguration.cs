using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class OrganizationPermissionConfiguration : IEntityTypeConfiguration<OrganizationPermission>
{
    public void Configure(EntityTypeBuilder<OrganizationPermission> builder)
    {
        builder.ToTable("organization_permissions");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Permission)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.HasOne(x => x.OrganizationMembership)
            .WithMany()
            .HasForeignKey(x => x.OrganizationMembershipId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.GrantedByUser)
            .WithMany()
            .HasForeignKey(x => x.GrantedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.OrganizationMembershipId, x.Permission })
            .IsUnique();

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
