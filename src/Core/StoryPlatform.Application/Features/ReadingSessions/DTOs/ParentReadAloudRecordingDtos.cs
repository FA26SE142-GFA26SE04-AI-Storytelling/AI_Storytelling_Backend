namespace StoryPlatform.Application.Features.ReadingSessions;

public class ParentReadAloudRecordingsDto
{
    public int SessionId { get; set; }
    /// <summary>Đoạn truyện theo thứ tự trang/đoạn, rồi đoạn luyện theo thời gian.</summary>
    public List<ParentReadAloudRecordingDto> Recordings { get; set; } = new();
}

public class ParentReadAloudRecordingDto
{
    public int RecordingId { get; set; }
    /// <summary>"Segment" (đoạn truyện) hoặc "Practice" (đoạn luyện sau truyện).</summary>
    public string Kind { get; set; } = string.Empty;
    public int? SegmentId { get; set; }
    public int? InteractionId { get; set; }
    /// <summary>Trang (scene) chứa đoạn; null nếu không còn tìm thấy.</summary>
    public int? SceneIndex { get; set; }
    public int? SegmentOrder { get; set; }
    /// <summary>Chữ của đoạn bé đọc.</summary>
    public string Text { get; set; } = string.Empty;
    public int GoodCount { get; set; }
    public int RetryCount { get; set; }
    public int SkippedCount { get; set; }
    public int TotalCount { get; set; }
    public decimal GoodPercent { get; set; }
    /// <summary>Link nghe, hết hạn sau 15 phút.</summary>
    public string AudioUrl { get; set; } = string.Empty;
    public DateTime RecordedAt { get; set; }
}
