using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ReadingSessions;

public static class StoryEmotionLabels
{
    public static string Label(StoryEmotion emotion) => emotion switch
    {
        StoryEmotion.Happy => "Vui",
        StoryEmotion.Funny => "Buồn cười",
        StoryEmotion.Surprised => "Ngạc nhiên",
        StoryEmotion.Excited => "Hồi hộp",
        StoryEmotion.Touched => "Cảm động",
        StoryEmotion.Sad => "Buồn",
        StoryEmotion.Scared => "Hơi sợ",
        _ => string.Empty
    };

    /// <summary>Đọc tên enum (không phân biệt hoa thường); từ chối chữ lạ và số (kể cả "1", "99").</summary>
    public static bool TryParse(string? text, out StoryEmotion emotion)
    {
        emotion = default;
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed) || !Enum.GetNames<StoryEmotion>().Contains(trimmed, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        return Enum.TryParse(trimmed, ignoreCase: true, out emotion) && Enum.IsDefined(emotion);
    }
}
