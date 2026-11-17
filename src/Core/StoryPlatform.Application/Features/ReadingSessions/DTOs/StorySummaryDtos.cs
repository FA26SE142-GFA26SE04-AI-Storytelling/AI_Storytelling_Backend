namespace StoryPlatform.Application.Features.ReadingSessions;

public sealed class StorySummaryRecordingRequest
{
    public byte[] Audio { get; init; } = [];
    public string? ContentType { get; init; }
}

public sealed class StorySummaryQuestionRequest
{
    public int SceneId { get; init; }
    /// <summary>Thứ tự tranh trong cảnh (illustrations[].beatOrder của màn đọc); null = hỏi về cả cảnh.</summary>
    public int? BeatOrder { get; init; }
    public string? Text { get; init; }
    public byte[] Audio { get; init; } = [];
    public string? ContentType { get; init; }
}

public sealed class StorySummaryQuestionSentDto
{
    public int SummaryId { get; init; }
    public int QuestionId { get; init; }
    public string Message { get; init; } = string.Empty;
}

public sealed class StorySummarySentDto
{
    public int SummaryId { get; init; }
    public string Message { get; init; } = string.Empty;
}

public sealed class ParentStorySummaryQueryDto
{
    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public sealed class ParentStorySummaryItemDto
{
    public int SummaryId { get; init; }
    public int SessionId { get; init; }
    public int StoryId { get; init; }
    public string Title { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    /// <summary>Link nghe có hạn (15 phút); null khi con không ghi âm.</summary>
    public string? RecordingUrl { get; init; }
    public DateTime? RecordingSentAt { get; init; }
    public DateTime? FlagsSentAt { get; init; }
    public int UnansweredCount { get; init; }
    public List<ParentStorySummaryFlagDto> Flags { get; init; } = new();
}

public sealed class ParentStorySummaryFlagDto
{
    public int QuestionId { get; init; }
    public int? BeatOrder { get; init; }
    public string? PictureText { get; init; }
    public string? QuestionText { get; init; }
    public string? QuestionAudioUrl { get; init; }
    public DateTime AskedAt { get; init; }
    public DateTime? AnsweredAt { get; init; }
    public int SceneId { get; init; }
    public int SceneIndex { get; init; }
    public string SceneText { get; init; } = string.Empty;
    public string? IllustrationUrl { get; init; }
}

public sealed class StorySummaryAnsweredRequestDto
{
    public bool Answered { get; set; }
}

public sealed class StorySummaryAnsweredDto
{
    public int QuestionId { get; init; }
    public DateTime? AnsweredAt { get; init; }
}
