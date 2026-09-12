using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class AssignmentRecipientConfiguration : IEntityTypeConfiguration<AssignmentRecipient>
{
    public void Configure(EntityTypeBuilder<AssignmentRecipient> builder)
    {
        builder.ToTable("assignment_recipients");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.HasOne(x => x.Assignment)
            .WithMany()
            .HasForeignKey(x => x.AssignmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ChildProfile)
            .WithMany()
            .HasForeignKey(x => x.ChildProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.AssignmentId, x.ChildProfileId })
            .IsUnique();

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
