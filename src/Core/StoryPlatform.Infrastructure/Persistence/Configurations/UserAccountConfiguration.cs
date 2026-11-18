using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class UserAccountConfiguration : IEntityTypeConfiguration<UserAccount>
{
    public void Configure(EntityTypeBuilder<UserAccount> builder)
    {
        builder.ToTable("user_accounts");

        builder.HasKey(u => u.Id);

        builder.Property(u => u.Username)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(u => u.Email)
            .IsRequired()
            .HasMaxLength(150);

        builder.Property(u => u.PasswordHash)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(u => u.FullName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(u => u.PhoneNumber)
            .HasMaxLength(20);

        builder.Property(u => u.Expertise)
            .HasMaxLength(200);

        builder.Property(u => u.AvatarUrl)
            .HasMaxLength(500);

        builder.Property(u => u.ResetTokenHash)
            .HasMaxLength(255);

        builder.Property(u => u.RefreshTokenHash)
            .HasMaxLength(64); // SHA-256 hex = 64 ký tự

        builder.Property(u => u.EmailVerificationTokenHash)
            .HasMaxLength(64); // SHA-256 hex = 64 ký tự

        builder.Property(u => u.PendingEmail)
            .HasMaxLength(150);

        builder.Property(u => u.PendingEmailOtpHash)
            .HasMaxLength(64);

        builder.Property(u => u.Role)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(u => u.Status)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(u => u.RoleBeforeAdmin)
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.Property(u => u.CreatorTier)
            .HasConversion<string>()
            .HasMaxLength(20)
            // Mặc định cho các tài khoản đã có khi thêm cột; entity mới luôn mang giá trị khác sentinel (0).
            .HasDefaultValue(CreatorTier.Standard)
            .HasSentinel((CreatorTier)0)
            .IsRequired();

        builder.HasIndex(u => u.Email)
            .IsUnique();

        builder.HasIndex(u => u.Username)
            .IsUnique();

        // Soft delete global filter
        builder.HasQueryFilter(u => !u.IsDeleted);
    }
}
