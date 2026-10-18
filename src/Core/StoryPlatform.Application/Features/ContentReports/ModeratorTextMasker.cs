using System.Text.RegularExpressions;

namespace StoryPlatform.Application.Features.ContentReports;

/// <summary>
/// Che thông tin cá nhân trong văn bản trả cho Moderator (S4 BR-S4-12): số điện thoại, email và biệt danh của trẻ.
/// Chỉ là quy tắc chữ đơn giản, không nhận diện theo ngữ cảnh.
/// </summary>
public static class ModeratorTextMasker
{
    public const string PhonePlaceholder = "[số điện thoại]";
    public const string EmailPlaceholder = "[email]";
    public const string ChildPlaceholder = "[tên bé]";

    private static readonly Regex Email = new(
        @"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}", RegexOptions.Compiled);

    // Số di động Việt Nam: bắt đầu bằng 0, 84 hoặc +84, theo sau 8–10 chữ số.
    private static readonly Regex Phone = new(
        @"(?<!\w)(?:\+?84|0)(?:[\s.\-]?\d){8,10}(?!\d)", RegexOptions.Compiled);

    public static string? Mask(string? text, string? childNickname)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        // Email trước để các chữ số trong phần tên email không bị coi là số điện thoại.
        var masked = Email.Replace(text, EmailPlaceholder);
        masked = Phone.Replace(masked, PhonePlaceholder);

        var nickname = childNickname?.Trim();
        if (!string.IsNullOrEmpty(nickname))
        {
            var pattern = $@"(?<!\w){Regex.Escape(nickname)}(?!\w)";
            masked = Regex.Replace(masked, pattern, ChildPlaceholder, RegexOptions.IgnoreCase);
        }

        return masked;
    }
}
