using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

public class QuizItem : BaseEntity
{
    public int StoryVersionId { get; set; }
    public virtual StoryVersion? StoryVersion { get; set; }

    public QuizType Type { get; set; }
    public string Question { get; set; } = string.Empty;
    public string? CorrectAnswer { get; set; }
    public string? Choices { get; set; }
    public string? LearningGoal { get; set; }
    public string? SourceQuote { get; set; }
    public int? SourceStart { get; set; }
    public int? SourceEnd { get; set; }
    public string? Explanation { get; set; }
    public string? Hint { get; set; }
    public string? OptionFeedbackJson { get; set; }
}
