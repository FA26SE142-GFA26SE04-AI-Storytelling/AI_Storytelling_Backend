using System.Security.Cryptography;
using System.Text;

namespace StoryPlatform.Application.Common.Security;

/// <summary>
/// Băm một chuỗi token bất kỳ (refresh token, reset-password token) bằng SHA-256
/// để lưu trong CSDL dưới dạng không thể đảo ngược. Dùng SHA-256 (không dùng BCrypt)
/// vì các luồng refresh-token/reset-password cần TRA CỨU theo đúng giá trị băm
/// (deterministic) — BCrypt sinh salt ngẫu nhiên mỗi lần gọi nên không tra cứu được.
/// </summary>
public static class TokenHasher
{
    public static string Hash(string rawToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        var builder = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes)
        {
            builder.Append(b.ToString("x2"));
        }
        return builder.ToString();
    }
}
