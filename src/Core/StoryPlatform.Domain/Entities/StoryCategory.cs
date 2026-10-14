using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

public class StoryCategory : BaseEntity
{
    public int StoryId { get; set; }
    public virtual Story? Story { get; set; }

    public int ContentCategoryId { get; set; }
    public virtual ContentCategory? ContentCategory { get; set; }

    public TagSource Source { get; set; } = TagSource.Ai;
}
