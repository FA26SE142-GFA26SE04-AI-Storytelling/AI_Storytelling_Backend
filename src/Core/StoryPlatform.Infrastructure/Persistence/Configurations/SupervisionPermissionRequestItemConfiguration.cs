using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class SupervisionPermissionRequestItemConfiguration : IEntityTypeConfiguration<SupervisionPermissionRequestItem>
{
    public void Configure(EntityTypeBuilder<SupervisionPermissionRequestItem> builder)
    {
        builder.ToTable("supervision_permission_request_items");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Permission)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.HasOne(x => x.SupervisionPermissionRequest)
            .WithMany(x => x.Items)
            .HasForeignKey(x => x.SupervisionPermissionRequestId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
