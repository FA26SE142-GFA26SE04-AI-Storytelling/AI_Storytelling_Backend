using System;

namespace StoryPlatform.Domain.Entities;

/// <summary>
/// Credential Easy Login lâu dài của một hồ sơ trẻ (Luồng 1, Bước 1.10, BR-1.15):
/// DB chỉ lưu hash của secret QR (SHA-256), không lưu secret, không có TTL.
/// </summary>
public class ChildAccessCredential : BaseEntity
{
    public int ChildProfileId { get; set; }
    public virtual ChildProfile? ChildProfile { get; set; }

    public string AvatarId { get; set; } = string.Empty;

    public string? EasyLoginSecretHash { get; set; }
    public DateTime? EasyLoginCreatedAt { get; set; }

    public int CreatedByUserId { get; set; }
    public virtual UserAccount? CreatedByUser { get; set; }
}
