using System;

namespace StoryPlatform.Domain.Entities;

public class QuizAttempt : BaseEntity
{
    public int ReadingSessionId { get; set; }
    public virtual ReadingSession? ReadingSession { get; set; }

    public int QuizItemId { get; set; }
    public virtual QuizItem? QuizItem { get; set; }

    public string? AnswerGiven { get; set; }
    public bool? IsCorrect { get; set; }
    public DateTime AnsweredAt { get; set; }
}
