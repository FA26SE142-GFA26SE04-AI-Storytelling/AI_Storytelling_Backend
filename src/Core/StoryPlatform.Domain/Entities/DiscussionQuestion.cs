namespace StoryPlatform.Domain.Entities;

public class DiscussionQuestion : BaseEntity
{
    public int StoryVersionId { get; set; }
    public virtual StoryVersion? StoryVersion { get; set; }

    public string Question { get; set; } = string.Empty;
    public bool IsMoralLesson { get; set; } = false;
}
