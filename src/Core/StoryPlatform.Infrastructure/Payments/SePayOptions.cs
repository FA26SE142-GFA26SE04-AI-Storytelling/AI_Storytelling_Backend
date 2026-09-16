namespace StoryPlatform.Infrastructure.Payments;

/// <summary>
/// Cấu hình SePay — đọc từ configuration section "SePaySettings".
/// WebhookApiKey đặt trong appsettings.Development.json (đã .gitignore) hoặc biến môi trường
/// SePaySettings__WebhookApiKey (production), không lưu trong source.
/// </summary>
public sealed class SePayOptions
{
    public const string SectionName = "SePaySettings";
    public string BankAccountNumber { get; set; } = string.Empty;
    public string BankCode { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string WebhookApiKey { get; set; } = string.Empty;
}
