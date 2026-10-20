namespace StoryPlatform.Application.Features.Moderation.Interfaces;

/// <summary>
/// Việc chạy nền của hàng đợi kiểm duyệt: trả mục bị giữ quá 2 giờ và cảnh báo mục quá hạn SLA.
/// </summary>
public interface IModerationSweepService
{
    /// <summary>Trả về Pending mọi mục Claimed quá 2 giờ. Trả số mục đã trả.</summary>
    Task<int> ReleaseExpiredClaimsAsync(CancellationToken cancellationToken = default);

    /// <summary>Trả về Pending mọi mục đang do một Moderator giữ (dùng khi vô hiệu hóa Moderator).</summary>
    Task<int> ReleaseClaimsOfModeratorAsync(int moderatorUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rút các mục duyệt roadmap còn Pending do người không còn hoạt động gửi và đưa roadmap về Draft.
    /// creatorUserId null nghĩa là quét tất cả người gửi.
    /// </summary>
    Task<int> WithdrawRoadmapReviewsOfInactiveCreatorsAsync(
        int? creatorUserId = null, CancellationToken cancellationToken = default);

    /// <summary>Gửi moderation_sla_warning tới mọi Moderator active cho mục quá hạn chưa cảnh báo. Trả số mục.</summary>
    Task<int> WarnOverdueSlaAsync(CancellationToken cancellationToken = default);
}
