namespace StoryPlatform.Domain.Enums;

/// <summary>
/// Loại thông báo theo Luồng 1 Mục 7b. Các giá trị legacy chỉ được giữ để
/// tương thích dữ liệu cũ và không dùng khi tạo thông báo mới.
/// </summary>
public enum NotificationType
{
    SupervisionInvite = 1,
    SupervisionInviteResult = 2,
    SupervisionRevoked = 3,
    PermissionRequest = 4,
    PermissionRequestResult = 5,
    OwnershipTransferRequest = 6,
    OwnershipTransferResult = 7,
    AccountSecurityChanged = 8,
    HoldModeAlert = 9,
    RecommendationAwaitingReview = 10,
    DataRequestSlaWarning = 11,
    DataRequestCompleted = 12,
    AdminRoleChanged = 13,
    ContentReported = 14,
    ModerationSlaWarning = 15,
    ContentReportResolved = 16,
    PaymentConfirmed = 17,
    ContentReportEscalated = 18,
    ContentModerationResult = 19,
    StoryModerationResult = 20,
    StoryArchivedByModeration = 21,
    CreatorContentFlagged = 22,
    CommunityLibraryReviewed = 23,
    StaffInvite = 25,
    RoadmapStoryAdded = 26,
    RoadmapStoryRemoved = 27,
    RoadmapCompleted = 28,
    /// <summary>Trẻ gửi điều ước (WF-L2D story_wish_submitted).</summary>
    StoryWishSubmitted = 29,
    /// <summary>Điều ước chờ 6 ngày chưa ai xử lý (WF-L2D story_wish_expiring).</summary>
    StoryWishExpiring = 30
}
