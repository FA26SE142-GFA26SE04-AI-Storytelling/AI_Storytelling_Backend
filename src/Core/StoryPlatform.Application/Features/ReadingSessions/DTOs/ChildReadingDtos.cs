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
    /// <summary>"Chỉ nghe truyện" của truyện này; true thì app không dừng để hỏi tương tác.</summary>
    public bool ListenOnly { get; set; }
    public List<ReadingSceneDto> Scenes { get; set; } = new();
    public List<ReadingVocabularyDto> Vocabulary { get; set; } = new();
}

public class ReadingSceneDto
{
    /// <summary>Dùng làm sceneIds khi trẻ đánh cờ chỗ còn thắc mắc cuối truyện.</summary>
    public int SceneId { get; set; }
    public int SceneIndex { get; set; }
    public string Text { get; set; } = string.Empty;
    public string? IllustrationUrl { get; set; }
    public List<ReadingSegmentDto> Segments { get; set; } = new();
    /// <summary>Tương tác hiện khi nghe hết scene này; null nếu không có.</summary>
    public ReadingInteractionDto? Interaction { get; set; }
    /// <summary>Trẻ đã thử tương tác này trong phiên hiện tại, nên mở lại thì không hỏi nữa.</summary>
    public bool AttemptedInSession { get; set; }
}

/// <summary>Tương tác lúc nghe cho trẻ. Không có đáp án: không IsMatch, CorrectOrder, HiddenWord, Explanation.</summary>
public class ReadingInteractionDto
{
    /// <summary>Dùng làm interactionId khi gửi câu trả lời.</summary>
    public int Id { get; set; }
    /// <summary>"Checkpoint", "RolePlay", "FillBlank", "EventOrdering" hoặc "PicturePrediction".</summary>
    public string Type { get; set; } = string.Empty;
    /// <summary>Checkpoint: "Prediction", "Emotion" hoặc "Opinion".</summary>
    public string? Kind { get; set; }
    /// <summary>Câu hỏi (Checkpoint, PicturePrediction).</summary>
    public string? Prompt { get; set; }
    /// <summary>RolePlay: nhân vật trẻ nói theo.</summary>
    public string? CharacterName { get; set; }
    /// <summary>RolePlay: câu thoại để trẻ nói theo.</summary>
    public string? ModelSentence { get; set; }
    /// <summary>RolePlay: audio của đoạn truyện chứa câu thoại.</summary>
    public string? ModelAudioUrl { get; set; }
    /// <summary>FillBlank: câu có ô trống "____".</summary>
    public string? MaskedSentence { get; set; }
    /// <summary>FillBlank: gợi ý, không chứa đáp án.</summary>
    public string? Hint { get; set; }
    /// <summary>PicturePrediction: phát ngay khi trẻ chọn hình.</summary>
    public string? WaitFeedback { get; set; }
    /// <summary>PicturePrediction: nghe hết scene này rồi mới phát phản hồi của hình đã chọn.</summary>
    public int? RevealSceneIndex { get; set; }
    /// <summary>Audio của cả tương tác theo AudioPartKey: prompt, wait, match, noMatch, hint.</summary>
    public Dictionary<string, string> AudioUrls { get; set; } = new();
    /// <summary>Thẻ của Checkpoint, PicturePrediction, EventOrdering (đã xáo). Rỗng với RolePlay, FillBlank.</summary>
    public List<ReadingInteractionOptionDto> Options { get; set; } = new();
}

public class ReadingInteractionOptionDto
{
    /// <summary>Dùng làm optionId hoặc trong orderedOptionIds khi gửi câu trả lời.</summary>
    public int Id { get; set; }
    public string Text { get; set; } = string.Empty;
    /// <summary>PicturePrediction: hình của lựa chọn.</summary>
    public string? ImageUrl { get; set; }
    /// <summary>Audio đọc chữ trên thẻ.</summary>
    public string? AudioUrl { get; set; }
    /// <summary>Phản hồi khi chọn thẻ này; chỉ Checkpoint và PicturePrediction.</summary>
    public string? Feedback { get; set; }
    public string? FeedbackAudioUrl { get; set; }
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
    public string? AudioUrl { get; set; }
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
    /// <summary>EXP vừa nhận khi hoàn thành (0 nếu đọc lại hoặc gọi lần hai).</summary>
    public int ExpGained { get; set; }
    /// <summary>Mã các huy hiệu vừa mở.</summary>
    public List<string> NewBadges { get; set; } = new();

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
    /// <summary>"Chỉ nghe truyện" của truyện này.</summary>
    public bool ListenOnly { get; set; }
}

public class SetFeelingRequestDto
{
    /// <summary>"VeryLike", "Like", "Neutral" hoặc "Dislike".</summary>
    public string? Feeling { get; set; }
}

public class ReadingFeelingDto
{
    public int SessionId { get; set; }
    public string Feeling { get; set; } = string.Empty;
    /// <summary>Nhãn tiếng Việt để hiển thị ("Rất thích", …).</summary>
    public string Label { get; set; } = string.Empty;
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
    public string? AudioUrl { get; set; }
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
    public string? AudioUrl { get; set; }
    public int StoryId { get; set; }
    public string StoryTitle { get; set; } = string.Empty;
    /// <summary>Cảnh đầu tiên có từ này, để nút "Đọc lại đoạn này" nhảy tới; null nếu không tìm thấy.</summary>
    public int? SceneIndex { get; set; }
    public DateTime CollectedAt { get; set; }
    /// <summary>EXP vừa nhận khi lưu từ (chỉ có ở kết quả lưu; 0 khi lưu lại từ đã xóa).</summary>
    public int ExpGained { get; set; }
    /// <summary>Mã huy hiệu vừa mở khi lưu từ.</summary>
    public List<string> NewBadges { get; set; } = new();
}
