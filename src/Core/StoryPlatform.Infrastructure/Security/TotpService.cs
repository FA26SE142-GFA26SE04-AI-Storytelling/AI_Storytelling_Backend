using OtpNet;
using StoryPlatform.Application.Abstractions.Security;

namespace StoryPlatform.Infrastructure.Security;

public sealed class TotpService : ITotpService
{
    private const int SecretByteLength = 20;

    public string GenerateSecret() =>
        Base32Encoding.ToString(KeyGeneration.GenerateRandomKey(SecretByteLength));

    public string BuildProvisioningUri(string secret, string accountEmail, string issuer)
    {
        var encodedIssuer = Uri.EscapeDataString(issuer);
        var encodedAccount = Uri.EscapeDataString(accountEmail);
        return $"otpauth://totp/{encodedIssuer}:{encodedAccount}?secret={secret}&issuer={encodedIssuer}&algorithm=SHA1&digits=6&period=30";
    }

    public bool VerifyCode(string secret, string code)
    {
        var totp = new Totp(Base32Encoding.ToBytes(secret));
        return totp.VerifyTotp(code, out _, VerificationWindow.RfcSpecifiedNetworkDelay);
    }
}
