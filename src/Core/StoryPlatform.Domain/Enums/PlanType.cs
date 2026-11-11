namespace StoryPlatform.Domain.Enums;

/// <summary>
/// Loại gói bán (Luồng 2). Legacy là gói Personal cũ, chỉ giữ để giao dịch cũ hiển thị đúng.
/// </summary>
public enum PlanType
{
    Legacy = 0,
    Reading = 1,
    Creative = 2,
    AddOn = 3,
    /// <summary>Gói Creator: +lượt AI và +lượt gửi duyệt, mua một lần, không hết hạn.</summary>
    CreatorPack = 4
}
