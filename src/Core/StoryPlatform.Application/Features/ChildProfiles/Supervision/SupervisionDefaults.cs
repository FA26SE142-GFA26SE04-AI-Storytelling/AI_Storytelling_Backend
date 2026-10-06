using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ChildProfiles.Supervision;

/// <summary>
/// Hằng số nghiệp vụ giám sát — Luồng 1 v33 (BR-1.3, BR-1.4, BR-1.7, BR-1.10, BR-1.13).
/// </summary>
public static class SupervisionDefaults
{
    public const int InvitationDefaultExpiryDays = 7;
    public const int InvitationMaxExpiryDays = 30;
    public const int InvitationOtpMaxAttempts = 5;

    public static readonly TimeSpan InvitationOtpTtl = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan InvitationOtpResendCooldown = TimeSpan.FromSeconds(60);

    /// <summary>Sau khi xác thực OTP, người được mời có chừng này thời gian để Accept/Reject.</summary>
    public static readonly TimeSpan InvitationVerifiedWindow = TimeSpan.FromMinutes(30);

    public static readonly TimeSpan OwnershipTransferTtl = TimeSpan.FromDays(7);

    /// <summary>Preset quyền khi Additional Supervisor Accept (BR-1.7, BR-1.10).</summary>
    public static readonly IReadOnlyList<Permission> DefaultPreset = new[]
    {
        Permission.ViewProgress,
        Permission.ViewResults,
        Permission.ReceiveReport,
        Permission.ApproveStory
    };
}
