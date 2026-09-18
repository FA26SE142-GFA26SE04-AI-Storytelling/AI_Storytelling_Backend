using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class OwnershipTransferRequestConfiguration : IEntityTypeConfiguration<OwnershipTransferRequest>
{
    public void Configure(EntityTypeBuilder<OwnershipTransferRequest> builder)
    {
        builder.ToTable("ownership_transfer_requests");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.HasOne(x => x.ChildProfile)
            .WithMany()
            .HasForeignKey(x => x.ChildProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.CurrentOwnerUser)
            .WithMany()
            .HasForeignKey(x => x.CurrentOwnerUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.TargetSupervisorUser)
            .WithMany()
            .HasForeignKey(x => x.TargetSupervisorUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
