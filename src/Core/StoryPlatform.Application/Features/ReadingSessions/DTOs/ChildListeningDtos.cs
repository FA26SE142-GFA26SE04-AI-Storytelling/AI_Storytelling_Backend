namespace StoryPlatform.Application.Features.ReadingSessions;

/// <summary>Câu trả lời của trẻ. Chỉ gửi trường của đúng loại tương tác.</summary>
public class SubmitListeningAttemptRequestDto
{
    /// <summary>Checkpoint, PicturePrediction: Id thẻ đã chọn.</summary>
    public int? OptionId { get; set; }
    /// <summary>FillBlank: chữ trẻ điền.</summary>
    public string? Answer { get; set; }
    /// <summary>EventOrdering: Id các thẻ theo thứ tự trẻ xếp.</summary>
    public List<int>? OrderedOptionIds { get; set; }
}

/// <summary>Phản hồi cho trẻ. Không có chữ "sai", không có EXP.</summary>
public class ListeningAttemptResultDto
{
    public int InteractionId { get; set; }
    /// <summary>Loại tương tác, như Type của ReadingInteractionDto.</summary>
    public string Type { get; set; } = string.Empty;
    /// <summary>FillBlank, EventOrdering; null với loại không so khớp.</summary>
    public bool? Matched { get; set; }
    /// <summary>Lời phản hồi phát cho trẻ (phản hồi của thẻ, lời khen hoặc lời mời nghe lại).</summary>
    public string? Feedback { get; set; }
    public string? FeedbackAudioUrl { get; set; }
    /// <summary>FillBlank chưa khớp: đáp án gợi ý.</summary>
    public string? SuggestedAnswer { get; set; }
    /// <summary>EventOrdering chưa khớp, chưa tới lần sai thứ 3: scene cần nghe lại.</summary>
    public List<int> ReplaySceneIndexes { get; set; } = new();
    /// <summary>EventOrdering: true từ lần sai thứ 3 trong phiên; FE hiện thứ tự đúng rồi đi tiếp.</summary>
    public bool Revealed { get; set; }
    /// <summary>Khi Revealed: Id các thẻ theo thứ tự đúng.</summary>
    public List<int>? CorrectOrderOptionIds { get; set; }
}
