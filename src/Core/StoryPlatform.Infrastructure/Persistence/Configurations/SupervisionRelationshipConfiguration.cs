using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class SupervisionRelationshipConfiguration : IEntityTypeConfiguration<SupervisionRelationship>
{
    public void Configure(EntityTypeBuilder<SupervisionRelationship> builder)
    {
        builder.ToTable("supervision_relationships");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.SupervisorRole)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.HasOne(x => x.ChildProfile)
            .WithMany()
            .HasForeignKey(x => x.ChildProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.SupervisorUser)
            .WithMany()
            .HasForeignKey(x => x.SupervisorUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.SupervisionInvitation)
            .WithMany()
            .HasForeignKey(x => x.SupervisionInvitationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.RevokedByUser)
            .WithMany()
            .HasForeignKey(x => x.RevokedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
