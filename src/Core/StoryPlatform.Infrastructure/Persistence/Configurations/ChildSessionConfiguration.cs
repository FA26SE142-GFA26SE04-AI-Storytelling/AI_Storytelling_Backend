using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class ChildSessionConfiguration : IEntityTypeConfiguration<ChildSession>
{
    public void Configure(EntityTypeBuilder<ChildSession> builder)
    {
        builder.ToTable("child_sessions");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.SessionKey)
            .IsRequired()
            .HasMaxLength(64);

        builder.HasOne(x => x.ChildProfile)
            .WithMany()
            .HasForeignKey(x => x.ChildProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.SessionKey)
            .IsUnique();

        builder.HasIndex(x => x.ChildProfileId);

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
