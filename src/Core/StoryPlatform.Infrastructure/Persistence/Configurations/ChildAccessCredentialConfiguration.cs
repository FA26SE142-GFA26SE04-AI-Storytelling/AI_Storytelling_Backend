using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class ChildAccessCredentialConfiguration : IEntityTypeConfiguration<ChildAccessCredential>
{
    public void Configure(EntityTypeBuilder<ChildAccessCredential> builder)
    {
        builder.ToTable("child_access_credentials");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.AvatarId)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.EasyLoginSecretHash)
            .HasMaxLength(64);

        builder.HasOne(x => x.ChildProfile)
            .WithMany()
            .HasForeignKey(x => x.ChildProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.CreatedByUser)
            .WithMany()
            .HasForeignKey(x => x.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Giữ dòng đã thu hồi cho audit; chỉ một credential đang hiệu lực cho mỗi hồ sơ.
        builder.HasIndex(x => x.ChildProfileId)
            .IsUnique()
            .HasFilter("\"RevokedAt\" IS NULL AND \"IsDeleted\" = false");

        builder.HasIndex(x => x.EasyLoginSecretHash)
            .IsUnique()
            .HasFilter("\"EasyLoginSecretHash\" IS NOT NULL");

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
