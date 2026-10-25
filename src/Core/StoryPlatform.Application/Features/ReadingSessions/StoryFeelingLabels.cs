using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ReadingSessions;

public static class StoryFeelingLabels
{
    public static string Label(StoryFeeling feeling) => feeling switch
    {
        StoryFeeling.VeryLike => "Rất thích",
        StoryFeeling.Like => "Thích",
        StoryFeeling.Neutral => "Bình thường",
        StoryFeeling.Dislike => "Chưa thích",
        _ => string.Empty
    };

    /// <summary>Đọc tên enum (không phân biệt hoa thường); từ chối chữ lạ và số (kể cả "1", "99").</summary>
    public static bool TryParse(string? text, out StoryFeeling feeling)
    {
        feeling = default;
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed) || char.IsDigit(trimmed[0]))
        {
            return false;
        }

        return Enum.TryParse(trimmed, ignoreCase: true, out feeling) && Enum.IsDefined(feeling);
    }
}
