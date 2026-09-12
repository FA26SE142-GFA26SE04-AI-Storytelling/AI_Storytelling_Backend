namespace StoryPlatform.Domain.Entities;

public class ReadingProgress : BaseEntity
{
    public int ChildProfileId { get; set; }
    public virtual ChildProfile? ChildProfile { get; set; }

    public int StoryId { get; set; }
    public virtual Story? Story { get; set; }

    public int LastPageRead { get; set; } = 1;
    public bool IsFavorited { get; set; } = false;
    public bool IsBookmarked { get; set; } = false;
}
