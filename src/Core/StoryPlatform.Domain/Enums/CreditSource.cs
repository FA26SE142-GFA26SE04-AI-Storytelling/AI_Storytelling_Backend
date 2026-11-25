namespace StoryPlatform.Domain.Enums;

/// <summary>
/// Nguồn cấp của một lô lượt. Legacy là phần lượt còn dư chuyển từ token_quota_configs.
/// </summary>
public enum CreditSource
{
    Trial = 1,
    Cycle = 2,
    AddOn = 3,
    Legacy = 4,
    /// <summary>Admin cấp cho một tài khoản (UC-L2-08); không hết hạn.</summary>
    AdminAdjust = 5,
    /// <summary>Hạn mức tháng miễn phí của Content Creator; hết hạn cuối tháng (giờ Việt Nam).</summary>
    CreatorMonthly = 6,
    /// <summary>Lượt mua từ Gói Creator; không hết hạn.</summary>
    CreatorPack = 7,
    /// <summary>Lượt tài trợ tạm thời trong phạm vi một Event (EV-12).</summary>
    EventGrant = 8,
    /// <summary>Permanent prize credits, usable outside the event that awarded them.</summary>
    EventReward = 9
}
