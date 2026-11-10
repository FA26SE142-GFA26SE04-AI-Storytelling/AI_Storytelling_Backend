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
    AdminAdjust = 5
}
