namespace StoryPlatform.Application.Features.ReadingSessions;

/// <summary>Kết quả của một chữ trong đoạn truyện: good, retry hoặc skipped.</summary>
public sealed record ReadAloudWordDto(int Index, string Text, string Status, string? Hint);


/// <summary>Đoạn ghi âm trẻ gửi lên; chỉ dùng trong bộ nhớ.</summary>
public sealed class ReadAloudRequest
{
    public byte[] Audio { get; init; } = [];
    public string? ContentType { get; init; }
}

public sealed class ReadAloudResultDto
{
    public int SessionId { get; set; }
    public int SegmentId { get; set; }
    public IReadOnlyList<ReadAloudWordDto> Words { get; set; } = [];
    public int GoodCount { get; set; }
    public int RetryCount { get; set; }
    public int SkippedCount { get; set; }
    public int TotalCount { get; set; }
    /// <summary>Lời khen hoặc gợi ý; không bao giờ có chữ "sai".</summary>
    public string Message { get; set; } = string.Empty;
}
