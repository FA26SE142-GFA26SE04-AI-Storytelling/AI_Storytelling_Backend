namespace StoryPlatform.Domain.Entities;

/// <summary>
/// Một câu bé hỏi bố mẹ trong bản tóm tắt cuối truyện: về một tranh của cảnh, hoặc cả cảnh khi cảnh không có tranh.
/// Tên giữ "flag" vì dòng cũ (chỉ có cảnh, không ghi âm/chữ) vẫn hợp lệ là câu hỏi về cả cảnh.
/// </summary>
public class StorySummaryFlag : BaseEntity
{
    public int StorySummaryId { get; set; }
    public virtual StorySummary? StorySummary { get; set; }

    public int StorySceneId { get; set; }
    public virtual StoryScene? StoryScene { get; set; }

    /// <summary>Tranh (beat) bé chọn; null = hỏi về cả cảnh.</summary>
    public int? IllustrationBeatId { get; set; }
    public virtual IllustrationBeat? IllustrationBeat { get; set; }

    /// <summary>Đúng tấm tranh bé đã thấy lúc hỏi; null khi cảnh không có tranh hoặc dòng cũ.</summary>
    public int? MediaAssetId { get; set; }
    public virtual MediaAsset? MediaAsset { get; set; }

    /// <summary>Chữ bé gõ (≤ 200 ký tự); null khi bé không gõ.</summary>
    public string? QuestionText { get; set; }

    /// <summary>Đường dẫn ghi âm câu hỏi trong storage; null khi bé không ghi âm hoặc đã xóa.</summary>
    public string? AudioStoragePath { get; set; }
    public string? AudioContentType { get; set; }
    public int? AudioBytes { get; set; }

    /// <summary>Lúc bố mẹ đánh dấu đã trả lời bé (ngoài app); null = chưa.</summary>
    public DateTime? AnsweredAt { get; set; }
}
