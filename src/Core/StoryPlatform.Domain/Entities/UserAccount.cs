using System;
using System.Collections.Generic;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

/// <summary>
/// Thực thể tài khoản người dùng (Phụ huynh, Giáo viên, Quản trị viên).
/// </summary>
public class UserAccount : BaseEntity
{
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? AvatarUrl { get; set; }
    public UserRole Role { get; set; } = UserRole.Parent;
    public AccountStatus Status { get; set; } = AccountStatus.Registered;
    public string? ResetTokenHash { get; set; }
    public DateTime? ResetTokenExpiresAt { get; set; }
    public DateTime? LastLoginAt { get; set; }

    // Navigation properties
    public virtual ICollection<Story> Stories { get; set; } = new List<Story>();
    public virtual ICollection<ChildProfile> ChildProfiles { get; set; } = new List<ChildProfile>();
}
