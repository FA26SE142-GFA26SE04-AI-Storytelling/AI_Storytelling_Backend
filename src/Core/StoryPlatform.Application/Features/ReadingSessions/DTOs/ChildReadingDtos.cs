namespace StoryPlatform.Application.Features.ReadingSessions;

public class ChildStoryQueryDto
{
    public string? Search { get; set; }
    /// <summary>"mine" = Truyện của em; "library" = Thư viện; để trống = cả hai.</summary>
    public string? Source { get; set; }
    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = ChildReadingPolicy.DefaultPageSize;
}

public class ChildStoryItemDto
{
    public int StoryId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? CoverImageUrl { get; set; }
    public string? Description { get; set; }
    public string Source { get; set; } = string.Empty;
}

public class ChildStoryPageDto
{
    public bool IsLocked { get; set; }
    /// <summary>Khi bị khóa: truyện cần đọc lại để mở khóa.</summary>
    public int? UnlockStoryId { get; set; }
    public string? Message { get; set; }
    public List<ChildStoryItemDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageIndex { get; set; }
    public int PageSize { get; set; }
}

public class StartReadingRequestDto
{
    public int? StoryId { get; set; }
    /// <summary>Mở truyện Cộng đồng: dùng mã nộp bài, không dùng StoryId gốc.</summary>
    public int? CommunitySubmissionId { get; set; }
}

public class ReadingSessionStartedDto
{
    public int SessionId { get; set; }
    public int StoryId { get; set; }
    public bool Resumed { get; set; }
    public int LastPage { get; set; }
    /// <summary>Chế độ đọc đã lưu gần nhất; null nếu trẻ chưa lưu chế độ.</summary>
    public string? LastReadingMode { get; set; }
    public bool IsBookmarked { get; set; }
    public bool IsFavorited { get; set; }
}

public class ReadingContentDto
{
    public int SessionId { get; set; }
    public int StoryId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Lesson { get; set; }
    public List<ReadingSceneDto> Scenes { get; set; } = new();
    public List<ReadingVocabularyDto> Vocabulary { get; set; } = new();
}

public class ReadingSceneDto
{
    public int SceneIndex { get; set; }
    public string Text { get; set; } = string.Empty;
    public string? IllustrationUrl { get; set; }
    public List<ReadingSegmentDto> Segments { get; set; } = new();
}

public class ReadingSegmentDto
{
    public int SegmentOrder { get; set; }
    public string Text { get; set; } = string.Empty;
    public string? AudioUrl { get; set; }
    /// <summary>JSON mốc thời gian từng từ, dùng cho karaoke.</summary>
    public string? WordTimingsJson { get; set; }
}

public class ReadingVocabularyDto
{
    public int Id { get; set; }
    public string Term { get; set; } = string.Empty;
}

public class SaveProgressRequestDto
{
    /// <summary>Trang hiện tại, bắt đầu từ 1 (= SceneIndex + 1).</summary>
    public int Page { get; set; }
    /// <summary>"ListenFirst" hoặc "ReadFirst".</summary>
    public string? ReadingMode { get; set; }
    public int AddedSeconds { get; set; }
}

public class ReadingProgressDto
{
    public int SessionId { get; set; }
    public int Page { get; set; }
    public string? ReadingMode { get; set; }
    public int TimeSpentSeconds { get; set; }
}

public class ReadingCompletedDto
{
    public int SessionId { get; set; }
    public int StoryId { get; set; }
    public bool IsRetake { get; set; }
    public bool RoadmapStepCompleted { get; set; }
}

public class SetFlagRequestDto
{
    public bool Value { get; set; }
}

public class ReadingFlagsDto
{
    public bool IsBookmarked { get; set; }
    public bool IsFavorited { get; set; }
}

public class ReadingEventRequestDto
{
    public string EventType { get; set; } = string.Empty;
    public string? Payload { get; set; }
}

public class VocabularyDefinitionDto
{
    public int VocabularyId { get; set; }
    public string Term { get; set; } = string.Empty;
    public string Definition { get; set; } = string.Empty;
    public bool IsCollected { get; set; }
}

public class CollectVocabularyRequestDto
{
    public int VocabularyId { get; set; }
}

public class NotebookEntryDto
{
    public int EntryId { get; set; }
    public int VocabularyId { get; set; }
    public string Term { get; set; } = string.Empty;
    public string Definition { get; set; } = string.Empty;
    public int StoryId { get; set; }
    public string StoryTitle { get; set; } = string.Empty;
    /// <summary>Cảnh đầu tiên có từ này, để nút "Đọc lại đoạn này" nhảy tới; null nếu không tìm thấy.</summary>
    public int? SceneIndex { get; set; }
    public DateTime CollectedAt { get; set; }
}
