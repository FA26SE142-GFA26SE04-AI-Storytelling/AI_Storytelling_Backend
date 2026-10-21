namespace StoryPlatform.Application.Features.CommunityLibrary.Interfaces;

/// <summary>Phía phụ huynh của thư viện cộng đồng (WF-L4F UC-L4F-01, 03).</summary>
public interface ICommunitySubmissionService
{
    /// <summary>Kiểm tra truyện có nộp Community được không và vì sao (không thay đổi dữ liệu).</summary>
    Task<CommunityEligibilityDto> CheckAsync(
        int actorUserId, CommunitySubmitRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Nộp truyện: ghim phiên bản hiện hành và tạo mục community_submission trong hàng đợi. Cần xác nhận không có
    /// thông tin định danh, đủ điều kiện và không còn thông tin cá nhân.
    /// </summary>
    Task<CommunitySubmitResultDto> SubmitAsync(
        int actorUserId, CommunitySubmitRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>Rút lần nộp khi chưa Moderator nào nhận (chỉ người nộp).</summary>
    Task<CommunitySubmissionDto> WithdrawAsync(
        int actorUserId, int itemId, CancellationToken cancellationToken = default);

    /// <summary>Gỡ truyện đã được duyệt khỏi thư viện cộng đồng (chỉ người nộp).</summary>
    Task<CommunitySubmissionDto> RemoveAsync(
        int actorUserId, int itemId, CancellationToken cancellationToken = default);

    /// <summary>Các lần nộp của tôi, mới nhất trước (tối đa 50).</summary>
    Task<List<CommunitySubmissionDto>> ListMineAsync(int actorUserId, CancellationToken cancellationToken = default);
}
