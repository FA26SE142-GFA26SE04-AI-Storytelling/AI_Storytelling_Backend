namespace StoryPlatform.Application.Abstractions.Security;

/// <summary>
/// Sinh và xác thực mã TOTP (RFC 6238) cho MFA — dùng cho Administrator (Bước 5.1, Mục 9).
/// </summary>
public interface ITotpService
{
    string GenerateSecret();

    string BuildProvisioningUri(string secret, string accountEmail, string issuer);

    bool VerifyCode(string secret, string code);
}
