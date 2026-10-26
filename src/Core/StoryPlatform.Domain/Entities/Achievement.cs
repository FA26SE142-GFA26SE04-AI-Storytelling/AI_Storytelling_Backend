namespace StoryPlatform.Domain.Entities;

public class Achievement : BaseEntity
{
    public int ChildProfileId { get; set; }
    public virtual ChildProfile? ChildProfile { get; set; }

    public int ExpTotal { get; set; } = 0;
    public int StreakDays { get; set; } = 0;

    /// <summary>Ngày (giờ Việt Nam) gần nhất trẻ hoàn thành một truyện; null nếu chưa có.</summary>
    public DateOnly? LastStreakDate { get; set; }
}
