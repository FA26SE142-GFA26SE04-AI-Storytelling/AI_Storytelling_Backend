using System.Security.Cryptography;
using System.Text;

namespace StoryPlatform.Application.Common.Security;

/// <summary>OTP 6 số dùng chung cho xác thực email và đổi email.</summary>
public static class OtpHelper
{
    public const string EmailVerifyPurpose = "email-verify";
    public const string EmailChangePurpose = "email-change";
    public const int MaxAttempts = 5;

    public static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan ResendCooldown = TimeSpan.FromSeconds(60);

    public static string Generate() =>
        RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    public static string Hash(string purpose, string scope, string otp) =>
        TokenHasher.Hash($"{purpose}:{scope}:{otp}");

    public static bool Matches(
        string? storedHash, string purpose, string scope, string? otp)
    {
        if (storedHash == null || string.IsNullOrWhiteSpace(otp))
        {
            return false;
        }

        var candidate = Hash(purpose, scope, otp.Trim());
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(storedHash), Encoding.UTF8.GetBytes(candidate));
    }
}
