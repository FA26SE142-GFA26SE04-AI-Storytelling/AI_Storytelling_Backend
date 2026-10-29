using System;
using System.Collections.Generic;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

/// <summary>
/// Thực thể tài khoản người dùng (Phụ huynh, Quản trị viên).
/// </summary>
public class UserAccount : BaseEntity
{
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? AvatarUrl { get; set; }
    public UserRole Role { get; set; } = UserRole.User;
    public AccountStatus Status { get; set; } = AccountStatus.Registered;
    public string? ResetTokenHash { get; set; }
    public DateTime? ResetTokenExpiresAt { get; set; }
    public string? RefreshTokenHash { get; set; }
    public DateTime? RefreshTokenExpiresAt { get; set; }
    public string? EmailVerificationTokenHash { get; set; }
    public DateTime? EmailVerificationTokenExpiresAt { get; set; }
    public DateTime? EmailVerifiedAt { get; set; }
    public int EmailVerificationFailedAttempts { get; set; }
    public DateTime? PasswordChangedAt { get; set; }
    public string? PendingEmail { get; set; }
    public string? PendingEmailOtpHash { get; set; }
    public DateTime? PendingEmailOtpExpiresAt { get; set; }
    public int PendingEmailOtpFailedAttempts { get; set; }
    public DateTime? LastLoginAt { get; set; }

    public int FailedLoginAttempts { get; set; } = 0;
    public DateTime? LockedUntil { get; set; }
    public int TokenVersion { get; set; } = 1;
    public UserRole? RoleBeforeAdmin { get; set; }

    /// <summary>Chuyên môn của Moderator / Content Creator (vd giáo viên tiểu học).</summary>
    public string? Expertise { get; set; }

    /// <summary>Thời điểm nhân sự được mời tick cam kết bảo mật dữ liệu trẻ em và quy tắc kiểm duyệt.</summary>
    public DateTime? CommitmentAcceptedAt { get; set; }

    // Navigation properties
    public virtual ICollection<Story> Stories { get; set; } = new List<Story>();
    public virtual ICollection<ChildProfile> ChildProfiles { get; set; } = new List<ChildProfile>();
}
