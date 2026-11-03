namespace StoryPlatform.Domain.Entities;

/// <summary>Cây tri thức của một hồ sơ trẻ (WF-L3A UC-L3A-08 bước 4). Mỗi trẻ một dòng.</summary>
public class KnowledgeTree : BaseEntity
{
    public int ChildProfileId { get; set; }
    public virtual ChildProfile? ChildProfile { get; set; }

    /// <summary>Tổng EXP trẻ đã tưới từ trước tới nay. EXP chưa tưới = Achievement.ExpTotal − giá trị này.</summary>
    public int ExpInvested { get; set; }

    /// <summary>EXP đã tưới vào cây đang trồng.</summary>
    public int CurrentTreeExp { get; set; }

    /// <summary>Số cây đã ra hoa và được đưa vào vườn.</summary>
    public int FloweredTreeCount { get; set; }

    public DateTime? LastWateredAt { get; set; }
}
