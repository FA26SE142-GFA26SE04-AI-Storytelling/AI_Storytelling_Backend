using System;

namespace StoryPlatform.Domain.Entities;

/// <summary>
/// Một phiên đăng nhập của Trẻ (qua PIN hoặc EasyLogin QR) — Bước 1.10.
/// Dùng để áp idle-timeout 20 phút; JWT expiry (ChildTokenExpiryMinutes) vẫn là trần tuyệt đối.
/// </summary>
public class ChildSession : BaseEntity
{
    public const string SessionClaimType = "child_session";
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(20);
    public static readonly TimeSpan ActivityWriteInterval = TimeSpan.FromMinutes(1);

    public int ChildProfileId { get; set; }
    public virtual ChildProfile? ChildProfile { get; set; }

    public string SessionKey { get; set; } = string.Empty;
    public DateTime LastActivityAt { get; set; }

    public bool IsIdleExpired(DateTime utcNow) => utcNow - LastActivityAt >= IdleTimeout;

    public bool ShouldRecordActivity(DateTime utcNow) => utcNow - LastActivityAt >= ActivityWriteInterval;
}
