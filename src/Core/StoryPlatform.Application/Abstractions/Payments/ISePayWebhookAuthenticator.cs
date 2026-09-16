namespace StoryPlatform.Application.Abstractions.Payments;

/// <summary>
/// Xác thực webhook SePay bằng API Key/Secret cấu hình qua biến môi trường.
/// </summary>
public interface ISePayWebhookAuthenticator
{
    bool IsValid(string? authorizationHeaderValue);
}
