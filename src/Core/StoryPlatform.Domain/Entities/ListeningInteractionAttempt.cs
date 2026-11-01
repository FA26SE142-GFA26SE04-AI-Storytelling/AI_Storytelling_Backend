namespace StoryPlatform.Domain.Entities;

/// <summary>Một lần trẻ thử một tương tác trong một phiên đọc ("đã thử"). Bỏ qua thì không có dòng nào.</summary>
public class ListeningInteractionAttempt : BaseEntity
{
    public int ReadingSessionId { get; set; }
    public virtual ReadingSession? ReadingSession { get; set; }

    public int ChildProfileId { get; set; }
    public virtual ChildProfile? ChildProfile { get; set; }

    public int ListeningInteractionId { get; set; }
    public virtual ListeningInteraction? ListeningInteraction { get; set; }

    public int? SelectedOptionId { get; set; }
    public virtual ListeningInteractionOption? SelectedOption { get; set; }

    public string? AnswerText { get; set; }
    public string? OrderedOptionIdsJson { get; set; }
    public bool? IsMatch { get; set; }
    public DateTime AttemptedAt { get; set; }
}
