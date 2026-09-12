using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class SupervisionPermissionConfiguration : IEntityTypeConfiguration<SupervisionPermission>
{
    public void Configure(EntityTypeBuilder<SupervisionPermission> builder)
    {
        builder.ToTable("supervision_permissions");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Permission)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.HasOne(x => x.SupervisionRelationship)
            .WithMany()
            .HasForeignKey(x => x.SupervisionRelationshipId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
