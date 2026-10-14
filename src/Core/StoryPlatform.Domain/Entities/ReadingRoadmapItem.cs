namespace StoryPlatform.Domain.Entities;

public class ReadingRoadmapItem : BaseEntity
{
    public int ReadingRoadmapId { get; set; }
    public virtual ReadingRoadmap? ReadingRoadmap { get; set; }

    public int StoryId { get; set; }
    public virtual Story? Story { get; set; }

    public int Position { get; set; }

    /// <summary>null = dùng ReadingRoadmap.DefaultDaysPerStory.</summary>
    public int? Days { get; set; }
}
