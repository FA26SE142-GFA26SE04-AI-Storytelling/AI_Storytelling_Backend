namespace StoryPlatform.Application.Features.ReadingSessions;

public sealed class StorySummaryRecordingRequest
{
    public byte[] Audio { get; init; } = [];
    public string? ContentType { get; init; }
}

public sealed class StorySummaryFlagsRequestDto
{
    public List<int>? SceneIds { get; set; }
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
    public List<ParentStorySummaryFlagDto> Flags { get; init; } = new();
}

public sealed class ParentStorySummaryFlagDto
{
    public int SceneId { get; init; }
    public int SceneIndex { get; init; }
    public string SceneText { get; init; } = string.Empty;
    public string? IllustrationUrl { get; init; }
}
