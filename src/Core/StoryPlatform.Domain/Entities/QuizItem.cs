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
}
