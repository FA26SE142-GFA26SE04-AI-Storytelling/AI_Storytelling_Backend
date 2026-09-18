using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class SupervisionPermissionRequestConfiguration : IEntityTypeConfiguration<SupervisionPermissionRequest>
{
    public void Configure(EntityTypeBuilder<SupervisionPermissionRequest> builder)
    {
        builder.ToTable("supervision_permission_requests");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.HasOne(x => x.SupervisionRelationship)
            .WithMany()
            .HasForeignKey(x => x.SupervisionRelationshipId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.RequesterUser)
            .WithMany()
            .HasForeignKey(x => x.RequesterUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.RespondedByUser)
            .WithMany()
            .HasForeignKey(x => x.RespondedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
