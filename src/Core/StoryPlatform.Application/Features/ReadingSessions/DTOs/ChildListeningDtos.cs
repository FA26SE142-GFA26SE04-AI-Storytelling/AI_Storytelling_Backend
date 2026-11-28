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
    public double? X { get; set; }
    public double? Y { get; set; }
    public bool? Truth { get; set; }
    public int? TargetOptionId { get; set; }
    /// <summary>AlternateReading: tên nhân vật bé chọn đóng.</summary>
    public string? Speaker { get; set; }
    public string? Verdict { get; set; }
    public int? ReasonOptionId { get; set; }
    public string? FreeText { get; set; }
    public bool FreeVoice { get; set; }
    public int? SentenceIndex { get; set; }
    public List<int>? KeptOptionIds { get; set; }
    public List<PlacementDto>? Placements { get; set; }
}

/// <summary>Phản hồi cho trẻ. Không có chữ "sai", không có EXP.</summary>
public class ListeningAttemptResultDto
{
    public string? Positive { get; set; }
    public string? Limitation { get; set; }
    public string? PositiveAudioUrl { get; set; }
    public string? LimitationAudioUrl { get; set; }
    public bool? SentenceCorrect { get; set; }
    public int? CorrectSentenceIndex { get; set; }
    public ClueSpanDto? SourceSpan { get; set; }
    public bool? TooLong { get; set; }
    public List<int>? MissingMainOptionIds { get; set; }
    public List<int>? ModelKeptOptionIds { get; set; }
    public List<string>? ModelSentenceAudioUrls { get; set; }
    public List<EditorExplanationDto>? Explanations { get; set; }
    public List<EdgeResultDto>? EdgeResults { get; set; }
    public int? OptionId { get; set; }
    public bool Completed { get; set; }
    public HotspotDto? RevealHotspot { get; set; }
    /// <summary>Tranh chứa RevealHotspot khi chi tiết nằm trong tranh khác tranh đang hiện (Đúng/sai chỉ vào bằng chứng).</summary>
    public string? RevealImageUrl { get; set; }
    /// <summary>Đúng/sai khi lộ đáp án: câu vừa trả lời đúng (true) hay không đúng (false) với truyện.</summary>
    public bool? CorrectTruth { get; set; }
    /// <summary>AlternateReading: nhân vật bé đã chọn.</summary>
    public string? Speaker { get; set; }
    /// <summary>InferenceQuestion: vị trí các manh mối trong đoạn (ký tự, [Start, End)); chỉ có sau khi trả lời.</summary>
    public List<ClueSpanDto>? ClueSpans { get; set; }
    /// <summary>InferenceQuestion, SceneTitle: lời giải thích khi đúng hoặc khi lộ đáp án.</summary>
    public string? Explanation { get; set; }
    public string? ExplanationAudioUrl { get; set; }
    /// <summary>ThoughtBubble: cảm xúc của bong bóng đúng để nhân vật biểu lộ; chỉ có khi đúng hoặc lộ đáp án.</summary>
    public string? RevealEmotion { get; set; }
    /// <summary>CauseEffect: thẻ lí do đúng của thẻ vừa nối, khi nối đúng hoặc khi hệ thống nối sẵn.</summary>
    public int? MatchedCauseOptionId { get; set; }
    /// <summary>CauseEffect: khi xong cả 3 thẻ, ba câu "Vì … nên …" (kèm audio).</summary>
    public List<ChainSentenceDto>? ChainSentences { get; set; }
    public string? SentenceAudioUrl { get; set; }
    public string? SentenceWordTimings { get; set; }
    public int? CorrectOptionId { get; set; }
    public bool CanRetry { get; set; }
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

public sealed record PlacementDto(int EdgeIndex, int CardOptionId);
public sealed record EdgeResultDto(int EdgeIndex, bool Matched, string? Explanation,
    string? ExplanationAudioUrl, int? AcceptedCardOptionId);
public sealed record EditorExplanationDto(int OptionId, string Text, string? AudioUrl);

public class ClueSpanDto
{
    public int Start { get; set; }
    public int End { get; set; }
}

public class ChainSentenceDto
{
    public int EffectOptionId { get; set; }
    public string Text { get; set; } = string.Empty;
    public string? AudioUrl { get; set; }
}

public class HotspotDto
{
    public double X { get; set; }
    public double Y { get; set; }
    public double W { get; set; }
    public double H { get; set; }
}

/// <summary>CharacterLetter: chỉ ghi đã thử hoặc bỏ qua, không chấm điểm.</summary>
public class ListeningStepsRequestDto
{
    public string? Sound { get; set; }
    public string? Trace { get; set; }
}
