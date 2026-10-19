using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.Moderation;

/// <summary>
/// Quy tắc cố định của hàng đợi kiểm duyệt (Moderator Mục 2): thời gian giữ mục, thời hạn quyền đọc tạm,
/// SLA và ưu tiên theo loại mục.
/// </summary>
public static class ModerationQueuePolicy
{
    /// <summary>Quá thời gian này kể từ lúc nhận mà chưa quyết định thì mục tự trả về hàng đợi.</summary>
    public static readonly TimeSpan ClaimTimeout = TimeSpan.FromHours(2);

    /// <summary>Thời hạn quyền đọc tạm sau khi bấm "Xem để rà soát".</summary>
    public static readonly TimeSpan ReviewViewLifetime = TimeSpan.FromHours(2);

    public static readonly TimeSpan ResubmissionPriorityWindow = TimeSpan.FromHours(24);

    public static TimeSpan GetSla(ModerationItemType itemType) => itemType switch
    {
        ModerationItemType.GrayZoneStory => TimeSpan.FromHours(24),
        ModerationItemType.ContentReport => TimeSpan.FromHours(48),
        _ => TimeSpan.FromHours(72)
    };

    public static ModerationPriority GetPriority(ModerationItemType itemType, bool isSafetyConcern) => itemType switch
    {
        ModerationItemType.GrayZoneStory => ModerationPriority.High,
        ModerationItemType.ContentReport => isSafetyConcern ? ModerationPriority.High : ModerationPriority.Medium,
        ModerationItemType.PublicStoryReview => ModerationPriority.Medium,
        _ => ModerationPriority.Low
    };
}
