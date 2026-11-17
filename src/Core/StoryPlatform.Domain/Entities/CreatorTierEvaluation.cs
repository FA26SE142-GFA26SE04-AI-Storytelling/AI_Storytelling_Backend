using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

/// <summary>
/// Kết quả xét cấp Creator đối tác của một tháng (Luồng 5, 5g): mỗi Creator tối đa một dòng mỗi tháng. Lưu số liệu của
/// 4 tiêu chí tại lúc xét để đếm "không đạt 2 tháng liên tiếp" và để Admin xem lại.
/// </summary>
public class CreatorTierEvaluation : BaseEntity
{
    public int CreatorUserId { get; set; }
    public virtual UserAccount? Creator { get; set; }

    /// <summary>00:00 ngày 1 của tháng được xét theo giờ Việt Nam, tính ra UTC.</summary>
    public DateTime PeriodMonth { get; set; }

    /// <summary>Cửa sổ 90 ngày của tiêu chí đọc, kết thúc lúc xét.</summary>
    public DateTime WindowStart { get; set; }
    public DateTime WindowEnd { get; set; }

    public int PublishedStories { get; set; }
    public decimal CompletionRate { get; set; }
    public int ValidReads { get; set; }
    public int ViolationRemovals { get; set; }

    /// <summary>Đạt cả 4 tiêu chí.</summary>
    public bool Eligible { get; set; }

    public CreatorTier TierBefore { get; set; }
    public CreatorTier TierAfter { get; set; }
}
