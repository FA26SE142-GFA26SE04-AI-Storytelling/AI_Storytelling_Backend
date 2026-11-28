namespace StoryPlatform.Domain.Entities;

/// <summary>
/// Bản ghi âm lần đọc to gần nhất của bé cho một đoạn truyện (StorySegmentId) hoặc một đoạn luyện
/// (ListeningInteractionId) trong một phiên. Chỉ chủ hồ sơ nghe; file lưu tới khi chủ hồ sơ xóa.
/// </summary>
public class ReadAloudRecording : BaseEntity
{
    public int ReadingSessionId { get; set; }
    public virtual ReadingSession? ReadingSession { get; set; }

    public int ChildProfileId { get; set; }

    /// <summary>Đoạn truyện bé đọc; null khi là đoạn luyện.</summary>
    public int? StorySegmentId { get; set; }
    public virtual StorySegment? StorySegment { get; set; }

    /// <summary>Đoạn luyện đọc to (ListeningInteraction loại ReadAloud); null khi là đoạn truyện.</summary>
    public int? ListeningInteractionId { get; set; }
    public virtual ListeningInteraction? ListeningInteraction { get; set; }

    public string AudioStoragePath { get; set; } = string.Empty;
    public string AudioContentType { get; set; } = string.Empty;
    public int AudioBytes { get; set; }

    public int GoodCount { get; set; }
    public int RetryCount { get; set; }
    public int SkippedCount { get; set; }
    public int TotalCount { get; set; }
    /// <summary>Per-word statuses only; never contains the child's transcription.</summary>
    public string? WordStatusesJson { get; set; }

    public DateTime RecordedAt { get; set; }
}
