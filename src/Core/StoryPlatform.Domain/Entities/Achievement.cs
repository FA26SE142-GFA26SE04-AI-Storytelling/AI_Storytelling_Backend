namespace StoryPlatform.Domain.Entities;

public class Achievement : BaseEntity
{
    public int ChildProfileId { get; set; }
    public virtual ChildProfile? ChildProfile { get; set; }

    public int ExpTotal { get; set; } = 0;
    public int StreakDays { get; set; } = 0;
}
