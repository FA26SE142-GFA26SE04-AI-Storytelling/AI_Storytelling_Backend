using System;

namespace StoryPlatform.Domain.Entities;

/// <summary>
/// Credential Easy Login lâu dài của một hồ sơ trẻ (Luồng 1, Bước 1.10, BR-1.15), không có TTL.
/// Đăng nhập so khớp bằng hash SHA-256 của secret QR. Secret còn được lưu dạng mã hóa (Data Protection,
/// khóa lưu trong DB) để phụ huynh xem lại mã QR đang dùng; không bao giờ lưu secret dạng rõ.
/// </summary>
public class ChildAccessCredential : BaseEntity
{
    public int ChildProfileId { get; set; }
    public virtual ChildProfile? ChildProfile { get; set; }

    public string AvatarId { get; set; } = string.Empty;

    public string? EasyLoginSecretHash { get; set; }

    /// <summary>Secret QR đã mã hóa để phụ huynh xem lại; null với mã tạo trước khi có cột này hoặc đã thu hồi.</summary>
    public string? EasyLoginSecretProtected { get; set; }
    public DateTime? EasyLoginCreatedAt { get; set; }
    public DateTime? RevokedAt { get; set; }

    public int CreatedByUserId { get; set; }
    public virtual UserAccount? CreatedByUser { get; set; }
}
