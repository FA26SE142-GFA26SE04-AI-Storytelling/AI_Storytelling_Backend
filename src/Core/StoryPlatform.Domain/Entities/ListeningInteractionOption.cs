namespace StoryPlatform.Domain.Entities;

/// <summary>
/// Thẻ trả lời (Checkpoint), đáp án chấp nhận (FillBlank), thẻ sự kiện (EventOrdering) hoặc hình (PicturePrediction).
/// </summary>
public class ListeningInteractionOption : BaseEntity
{
    public int ListeningInteractionId { get; set; }
    public virtual ListeningInteraction? ListeningInteraction { get; set; }

    public int SortOrder { get; set; }
    public string Text { get; set; } = string.Empty;
    public string? Feedback { get; set; }
    public bool IsMatch { get; set; }
    public int? CorrectOrder { get; set; }
    public string? SourceQuote { get; set; }
    public int? SourceStart { get; set; }
    public int? SourceEnd { get; set; }
    public string? ImageDescription { get; set; }
}
