namespace StoryPlatform.Domain.Entities;

public class StoryVocabulary : BaseEntity
{
    public int StoryVersionId { get; set; }
    public virtual StoryVersion? StoryVersion { get; set; }

    public string Term { get; set; } = string.Empty;
    public string Definition { get; set; } = string.Empty;
}
