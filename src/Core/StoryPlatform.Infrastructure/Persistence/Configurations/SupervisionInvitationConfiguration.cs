using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class SupervisionInvitationConfiguration : IEntityTypeConfiguration<SupervisionInvitation>
{
    public void Configure(EntityTypeBuilder<SupervisionInvitation> builder)
    {
        builder.ToTable("supervision_invitations");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.InvitationCode)
            .HasMaxLength(100);

        builder.Property(x => x.InviteeEmail)
            .HasMaxLength(150);

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.HasOne(x => x.ChildProfile)
            .WithMany()
            .HasForeignKey(x => x.ChildProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.InviterUser)
            .WithMany()
            .HasForeignKey(x => x.InviterUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.InviteeUser)
            .WithMany()
            .HasForeignKey(x => x.InviteeUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.InvitationCode)
            .IsUnique();

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
