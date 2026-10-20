using System.Text.RegularExpressions;

namespace StoryPlatform.Application.Features.CommunityLibrary;

/// <summary>Một chỗ nghi chứa thông tin cá nhân. Excerpt chỉ để người nộp tự tìm, không được lưu lại.</summary>
public sealed record PiiFinding(string Kind, string Location, string Excerpt);

public static class PiiKinds
{
    public const string Phone = "phone";
    public const string Email = "email";
    public const string Url = "url";
    public const string SocialHandle = "social_handle";
    public const string Address = "address";
    public const string School = "school";
    public const string ClassName = "class";
    public const string ChildNickname = "child_nickname";
}

/// <summary>
/// Dò thông tin cá nhân bằng quy tắc chữ (S4 Mục 3), không dùng AI và không nhận diện theo ngữ cảnh: số điện thoại,
/// email, URL / mạng xã hội, địa chỉ, tên trường, lớp và biệt danh của chính bé.
/// </summary>
public static class PiiDetector
{
    private const int ExcerptRadius = 20;

    private static readonly Regex Email = new(
        @"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}", RegexOptions.Compiled);

    // Số di động Việt Nam: bắt đầu bằng 0, 84 hoặc +84, theo sau 8–10 chữ số (cho phép khoảng trắng, chấm, gạch).
    private static readonly Regex Phone = new(
        @"(?<!\w)(?:\+?84|0)(?:[\s.\-]?\d){8,10}(?!\d)", RegexOptions.Compiled);

    private static readonly Regex Url = new(
        @"(?:https?://|www\.)\S+|(?<!\w)(?:facebook|fb|zalo|tiktok|instagram|youtube)\.(?:com|me|vn)\S*",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex Handle = new(@"(?<![\w.@])@[A-Za-z0-9_.]{3,}", RegexOptions.Compiled);

    // Số nhà + đường / phố / ngõ / ngách / hẻm + tên.
    private static readonly Regex StreetAddress = new(
        @"\b\d+[A-Za-z]?(?:/\d+)*\s+(?:đường|phố|ngõ|ngách|hẻm)\s+\p{L}+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Đơn vị hành chính + số hoặc tên viết hoa ("xã hội" viết thường không bị bắt).
    private static readonly Regex AdminArea = new(
        @"\b(?:[Pp]hường|[Qq]uận|[Hh]uyện|[Xx]ã|[Tt]hị trấn)\s+(?:\d+|\p{Lu}\p{L}*)", RegexOptions.Compiled);

    private static readonly Regex School = new(
        @"\btrường\s+(?:tiểu\s*học|thcs|thpt|mầm\s*non|mẫu\s*giáo|th)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ClassName = new(
        @"\blớp\s*\d{1,2}\s?[A-Za-z]\d{0,2}\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static List<PiiFinding> Scan(IEnumerable<(string Location, string? Text)> parts, string? childNickname)
    {
        var findings = new List<PiiFinding>();
        var nickname = childNickname?.Trim();
        var nicknameRegex = string.IsNullOrEmpty(nickname)
            ? null
            : new Regex($@"(?<!\w){Regex.Escape(nickname)}(?!\w)", RegexOptions.IgnoreCase);

        foreach (var (location, text) in parts)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            // Email trước và làm trống để phần tên email không bị coi là số điện thoại hay tên tài khoản.
            var scanned = Email.Replace(text, match =>
            {
                findings.Add(Make(PiiKinds.Email, location, text, match));
                return new string(' ', match.Length);
            });

            Add(findings, PiiKinds.Url, location, scanned, Url);
            scanned = Url.Replace(scanned, match => new string(' ', match.Length));
            Add(findings, PiiKinds.Phone, location, scanned, Phone);
            Add(findings, PiiKinds.SocialHandle, location, scanned, Handle);
            Add(findings, PiiKinds.Address, location, scanned, StreetAddress);
            Add(findings, PiiKinds.Address, location, scanned, AdminArea);
            Add(findings, PiiKinds.School, location, scanned, School);
            Add(findings, PiiKinds.ClassName, location, scanned, ClassName);
            if (nicknameRegex != null)
            {
                Add(findings, PiiKinds.ChildNickname, location, scanned, nicknameRegex);
            }
        }

        return findings;
    }

    private static void Add(List<PiiFinding> findings, string kind, string location, string text, Regex regex)
    {
        foreach (Match match in regex.Matches(text))
        {
            findings.Add(Make(kind, location, text, match));
        }
    }

    private static PiiFinding Make(string kind, string location, string text, Match match)
    {
        var start = Math.Max(0, match.Index - ExcerptRadius);
        var end = Math.Min(text.Length, match.Index + match.Length + ExcerptRadius);
        return new PiiFinding(kind, location, text[start..end].Trim());
    }
}
