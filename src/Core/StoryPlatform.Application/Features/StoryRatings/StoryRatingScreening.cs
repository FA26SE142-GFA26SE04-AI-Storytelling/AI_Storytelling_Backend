using System.Text;
using System.Text.RegularExpressions;
using StoryPlatform.Application.Features.ContentReports;

namespace StoryPlatform.Application.Features.StoryRatings;

/// <summary>Kết quả lọc một nhận xét: nội dung đã che và có cần Moderator xem trước khi hiện không.</summary>
public sealed record StoryRatingScreeningResult(string? Comment, bool NeedsModeration, string ReasonCode);

/// <summary>
/// Lọc tự động nhận xét đánh giá truyện (spec §4.4), chỉ dùng quy tắc chữ, không gọi AI:
/// che số điện thoại / email, rồi giữ lại cho Moderator khi có link, từ cấm hoặc ≤ 2 sao kèm chữ.
/// </summary>
public static partial class StoryRatingScreening
{
    public const string Version = "story-rating-rules-v1";
    public const string Clean = "CLEAN";
    public const string Link = "LINK";
    public const string BlockedTerm = "BLOCKED_TERM";
    public const string LowRatingWithText = "LOW_RATING_WITH_TEXT";
    public const int LowRatingThreshold = 2;

    // Danh sách cố định của Version hiện tại; đổi danh sách thì tăng Version.
    private static readonly string[] BlockedTerms =
    [
        "địt", "đụ", "lồn", "cặc", "buồi", "đéo", "đm", "đcm", "dm", "dcm", "vcl", "vkl", "clgt",
        "óc chó", "mẹ mày", "con chó", "súc vật", "khốn nạn", "thằng ngu", "con ngu", "ngu học",
        "fuck", "shit", "bitch"
    ];

    // So khớp nguyên từ / cụm từ: hai bên không được là chữ hoặc số.
    private static readonly Regex[] BlockedPatterns = BlockedTerms
        .Select(term => new Regex(
            $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(term.Normalize(NormalizationForm.FormC))}(?![\p{{L}}\p{{N}}])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled))
        .ToArray();

    public static StoryRatingScreeningResult Screen(int rating, string? comment)
    {
        var trimmed = comment?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return new StoryRatingScreeningResult(null, false, Clean);
        }

        var masked = ModeratorTextMasker.Mask(trimmed.Normalize(NormalizationForm.FormC), null)!;
        if (LinkPattern().IsMatch(masked))
        {
            return new StoryRatingScreeningResult(masked, true, Link);
        }

        if (BlockedPatterns.Any(pattern => pattern.IsMatch(masked)))
        {
            return new StoryRatingScreeningResult(masked, true, BlockedTerm);
        }

        return rating <= LowRatingThreshold
            ? new StoryRatingScreeningResult(masked, true, LowRatingWithText)
            : new StoryRatingScreeningResult(masked, false, Clean);
    }

    [GeneratedRegex(
        @"https?://|www\.|(?<![\p{L}\p{N}])[\p{L}\p{N}-]+\.(com|vn|net|org|info|io|me|xyz|link|ly)(?![\p{L}\p{N}])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LinkPattern();
}
