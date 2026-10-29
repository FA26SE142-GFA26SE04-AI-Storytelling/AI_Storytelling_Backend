namespace StoryPlatform.Infrastructure.Communication;

/// <summary>
/// Cấu hình cho ResendEmailSender — đọc từ configuration section "ResendSettings".
/// ApiKey đặt trong appsettings.Development.json (dev, file này đã bị .gitignore chặn
/// hoàn toàn trong repo này) hoặc biến môi trường ResendSettings__ApiKey (production).
/// StaffActivationUrl là URL trang kích hoạt của frontend; trang đọc email và inviteToken
/// từ fragment của liên kết rồi POST tới /api/v1/Auth/staff-activation.
/// </summary>
public sealed class ResendOptions
{
    public const string SectionName = "ResendSettings";
    public string ApiKey { get; set; } = string.Empty;
    public string Endpoint { get; set; } = "https://api.resend.com/emails";
    public string FromEmail { get; set; } = "no-reply@taletale.site";
    public string FromName { get; set; } = "MagicTales";
    public string StaffActivationUrl { get; set; } = string.Empty;
}
