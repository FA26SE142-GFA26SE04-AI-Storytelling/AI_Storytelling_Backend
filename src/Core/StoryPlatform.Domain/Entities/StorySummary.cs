namespace StoryPlatform.Domain.Entities;

/// <summary>
/// Tóm tắt cuối truyện trẻ gửi bố mẹ (Luồng 3 — Danh sách tương tác của trẻ, mục 6). Một dòng cho mỗi phiên đọc;
/// có bản ghi âm và/hoặc các cảnh trẻ đánh cờ. File lưu tới khi chủ hồ sơ xóa. Không phân tích bằng AI.
/// </summary>
public class StorySummary : BaseEntity
{
    public int ReadingSessionId { get; set; }
    public virtual ReadingSession? ReadingSession { get; set; }

    public int ChildProfileId { get; set; }
    public virtual ChildProfile? ChildProfile { get; set; }

    public int StoryId { get; set; }
    public virtual Story? Story { get; set; }

    public int? StoryVersionId { get; set; }

    /// <summary>Đường dẫn trong storage; null khi chưa ghi âm hoặc đã xóa.</summary>
    public string? AudioStoragePath { get; set; }
    public string? AudioContentType { get; set; }
    public int? AudioBytes { get; set; }
    public DateTime? RecordingSentAt { get; set; }

    public DateTime? FlagsSentAt { get; set; }
}
