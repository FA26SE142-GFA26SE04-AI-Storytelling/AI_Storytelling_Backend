namespace StoryPlatform.Domain.Entities;

/// <summary>Một cảnh trẻ đánh cờ "còn thắc mắc" trong bản tóm tắt cuối truyện.</summary>
public class StorySummaryFlag : BaseEntity
{
    public int StorySummaryId { get; set; }
    public virtual StorySummary? StorySummary { get; set; }

    public int StorySceneId { get; set; }
    public virtual StoryScene? StoryScene { get; set; }
}
