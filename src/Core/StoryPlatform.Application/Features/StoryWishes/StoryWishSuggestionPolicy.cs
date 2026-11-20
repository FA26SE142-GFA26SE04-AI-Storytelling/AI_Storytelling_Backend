namespace StoryPlatform.Application.Features.StoryWishes;

/// <summary>Bảng điểm gợi ý truyện có sẵn (WF-L2D UC-L2D-03). Không gọi AI.</summary>
public static class StoryWishSuggestionPolicy
{
    public const int MaxSuggestions = 3;
    public const int MinScore = 4;

    public const int CharacterPoints = 3;
    public const int PlacePoints = 2;
    public const int ChallengePoints = 1;
    public const int LessonPoints = 1;
    public const int NotFinishedPoints = 1;
    /// <summary>Truyện "Thử thách": khó hơn trình độ của trẻ từ 2 bậc trở lên.</summary>
    public const int TooHardPoints = -2;
    public const int TooHardGap = 2;

    /// <summary>Truyện cần ít nhất ngần này lượt cảm nhận có cảm xúc mới có "hồ sơ cảm xúc".</summary>
    public const int MinStoryReflections = 3;
    /// <summary>Cảm xúc được gắn cho truyện khi có trong ít nhất ngần này phần trăm số lượt cảm nhận.</summary>
    public const int EmotionTagSharePercent = 40;
    /// <summary>Số lần đọc xong gần nhất (có mức thích và cảm xúc) dùng để biết bé thích/né cảm xúc nào.</summary>
    public const int RecentReadsForEmotion = 10;
    /// <summary>Điểm xếp hạng khi truyện hay gây ra cảm xúc bé thích (không dùng để xét MinScore).</summary>
    public const int LikedEmotionPoints = 1;
    /// <summary>Điểm xếp hạng khi truyện hay gây ra cảm xúc bé né; né thắng thích (không dùng để xét MinScore).</summary>
    public const int AvoidedEmotionPoints = -2;

    public const string NoSuggestionMessage = "Chưa có truyện phù hợp trong thư viện";

    /// <summary>Dòng nhắc cho phụ huynh khi bé đã đọc xong và thích một truyện gợi ý.</summary>
    public static string LikedHint(string title, string feelingLabel) =>
        $"Bé đã đọc {title} và {feelingLabel.ToLowerInvariant()} — có thể dùng truyện này cho điều ước";
}
