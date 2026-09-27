using System;

namespace StoryPlatform.Domain.Entities;

/// <summary>
/// Một phiên của Trẻ (Bước 1.10 / Luồng 3 Bước 3.0) — qua PIN, EasyLogin QR hoặc Supervisor bàn giao thiết bị.
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

    // Bước 3.0 — đúng 1 trong 2 có giá trị (CHECK CK_child_sessions_exactly_one_entry_source):
    // trẻ tự vào bằng PIN/EasyLogin HOẶC Supervisor bàn giao thiết bị qua ProfileSwitcher.
    public int? ChildAccessCredentialId { get; set; }
    public virtual ChildAccessCredential? ChildAccessCredential { get; set; }

    public int? SupervisorSessionId { get; set; }
    public virtual RefreshToken? SupervisorSession { get; set; }

    public bool IsIdleExpired(DateTime utcNow) => utcNow - LastActivityAt >= IdleTimeout;

    public bool ShouldRecordActivity(DateTime utcNow) => utcNow - LastActivityAt >= ActivityWriteInterval;
}
