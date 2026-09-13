using System;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

public class RefreshToken : BaseEntity
{
    public int UserAccountId { get; set; }
    public virtual UserAccount? UserAccount { get; set; }

    public string TokenHash { get; set; } = string.Empty;
    public SessionScope SessionScope { get; set; }
    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
}
