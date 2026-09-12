namespace StoryPlatform.Infrastructure.Communication;

/// <summary>
/// Cấu hình cho ResendEmailSender — đọc từ configuration section "ResendSettings".
/// ApiKey đặt trong appsettings.Development.json (dev, file này đã bị .gitignore chặn
/// hoàn toàn trong repo này) hoặc biến môi trường ResendSettings__ApiKey (production).
/// </summary>
public sealed class ResendOptions
{
    public const string SectionName = "ResendSettings";
    public string ApiKey { get; set; } = string.Empty;
    public string Endpoint { get; set; } = "https://api.resend.com/emails";
    public string FromEmail { get; set; } = "tranduy1632004@gmail.com";
    public string FromName { get; set; } = "AI Storytelling Platform";
}
