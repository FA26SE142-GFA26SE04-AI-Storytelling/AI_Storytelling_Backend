using StoryPlatform.Application.Features.AccessControl;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.Moderation;

/// <summary>
/// Quy tắc cố định của hàng đợi kiểm duyệt (Moderator Mục 2): thời gian giữ mục, thời hạn quyền đọc tạm,
/// SLA và ưu tiên theo loại mục.
/// </summary>
public static class ModerationQueuePolicy
{
    internal static string? DisplayName(UserAccount? user) =>
        user == null ? null : string.IsNullOrWhiteSpace(user.FullName)
            ? (string.IsNullOrWhiteSpace(user.Username) ? null : user.Username)
            : user.FullName;
    /// <summary>Quá thời gian này kể từ lúc nhận mà chưa quyết định thì mục tự trả về hàng đợi.</summary>
    public static readonly TimeSpan ClaimTimeout = TimeSpan.FromHours(2);

    /// <summary>Thời hạn quyền đọc tạm sau khi bấm "Xem để rà soát".</summary>
    public static readonly TimeSpan ReviewViewLifetime = TimeSpan.FromHours(2);

    public static readonly TimeSpan ResubmissionPriorityWindow = TimeSpan.FromHours(24);

    /// <summary>
    /// Quyền cần có để nhận một loại mục. Báo cáo nội dung và đánh giá truyện đòi quyền chỉ Moderator giữ, nên
    /// Administrator không nhận được kể cả khi không còn Moderator (Luồng 5).
    /// </summary>
    public static string ClaimPermission(ModerationItemType itemType) => itemType switch
    {
        ModerationItemType.PublicStoryReview => Permissions.Review.PublicStory,
        ModerationItemType.GrayZoneStory => Permissions.Review.GrayZone,
        ModerationItemType.ContentReport => Permissions.Moderation.ReportReview,
        ModerationItemType.RoadmapTemplateReview => Permissions.Review.Roadmap,
        ModerationItemType.PublicStoryRemoval => Permissions.Review.CreatorPublication,
        ModerationItemType.StoryRating => Permissions.Moderation.RatingModerate,
        _ => Permissions.Moderation.QueueProcess,
    };

    public static TimeSpan GetSla(ModerationItemType itemType) => itemType switch
    {
        ModerationItemType.GrayZoneStory => TimeSpan.FromHours(24),
        ModerationItemType.ContentReport => TimeSpan.FromHours(48),
        _ => TimeSpan.FromHours(72)
    };

    /// <param name="isPartnerCreator">Truyện gửi duyệt của Creator đối tác được ưu tiên Cao (Luồng 5, 5g); checklist không đổi.</param>
    public static ModerationPriority GetPriority(
        ModerationItemType itemType, bool isSafetyConcern, bool isPartnerCreator = false) => itemType switch
    {
        ModerationItemType.GrayZoneStory => ModerationPriority.High,
        ModerationItemType.ContentReport => isSafetyConcern ? ModerationPriority.High : ModerationPriority.Medium,
        ModerationItemType.PublicStoryReview => isPartnerCreator ? ModerationPriority.High : ModerationPriority.Medium,
        _ => ModerationPriority.Low
    };
}
