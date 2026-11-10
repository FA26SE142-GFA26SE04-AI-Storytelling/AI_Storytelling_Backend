namespace StoryPlatform.Domain.Entities;

/// <summary>
/// Một lần Admin cấp (Amount dương) hoặc trừ (Amount âm) lượt cho một tài khoản, luôn kèm lý do (UC-L2-08).
/// </summary>
public class CreditAdjustment : BaseEntity
{
    public int UserId { get; set; }
    public virtual UserAccount? User { get; set; }

    public int Amount { get; set; }
    public string Reason { get; set; } = string.Empty;

    public int AdminId { get; set; }
    public virtual UserAccount? Admin { get; set; }
}
