namespace StoryPlatform.Domain.Entities;

/// <summary>
/// Một truyện có sẵn được gợi ý cho một điều ước (WF-L2D UC-L2D-03). Lưu cố định lúc gửi, không chạy lại,
/// để trẻ và phụ huynh thấy cùng danh sách, cùng thứ tự.
/// </summary>
public class StoryWishSuggestion : BaseEntity
{
    public int StoryWishId { get; set; }
    public virtual StoryWish? StoryWish { get; set; }

    public int StoryId { get; set; }
    public virtual Story? Story { get; set; }

    /// <summary>Có giá trị khi là truyện Cộng đồng: mở bằng mã này, không lộ id truyện gốc.</summary>
    public int? CommunitySubmissionId { get; set; }

    /// <summary>1, 2 hoặc 3.</summary>
    public int Rank { get; set; }

    public int Score { get; set; }

    /// <summary>Id các thẻ trùng khớp, cách nhau bằng dấu phẩy, theo thứ tự nhân vật → nơi chốn → thử thách → bài học.</summary>
    public string MatchedCardIds { get; set; } = string.Empty;
}
