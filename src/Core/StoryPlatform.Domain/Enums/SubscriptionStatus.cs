namespace StoryPlatform.Domain.Enums;

/// <summary>
/// Trạng thái một chu kỳ gói. Gia hạn sớm tạo dòng Scheduled; nâng gói chuyển dòng cũ sang Superseded.
/// </summary>
public enum SubscriptionStatus
{
    Scheduled = 1,
    Active = 2,
    Superseded = 3,
    Expired = 4
}
