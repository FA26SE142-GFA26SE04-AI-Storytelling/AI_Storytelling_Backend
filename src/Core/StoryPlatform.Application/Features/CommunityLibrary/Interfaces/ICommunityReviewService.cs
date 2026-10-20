namespace StoryPlatform.Application.Features.CommunityLibrary.Interfaces;

/// <summary>Phía Moderator của thư viện cộng đồng (WF-L4F UC-L4F-02, SCR-L4F-02).</summary>
public interface ICommunityReviewService
{
    /// <summary>Mở gói duyệt: đúng bản đã ghim, nhãn, kết quả dò PII, checklist và các khóa Approve.</summary>
    Task<CommunityReviewPackageDto> GetPackageAsync(
        int actorUserId, int itemId, CancellationToken cancellationToken = default);

    /// <summary>Lưu (một phần) checklist 3 tiêu chí.</summary>
    Task<List<CommunityChecklistEntryDto>> SaveChecklistAsync(
        int actorUserId, int itemId, SaveCommunityChecklistRequestDto request,
        CancellationToken cancellationToken = default);

    /// <summary>Sửa nhãn nội dung của truyện (ít nhất một nhãn, nhãn còn hoạt động).</summary>
    Task<List<CommunityLabelDto>> SetLabelsAsync(
        int actorUserId, int itemId, SetCommunityLabelsRequestDto request,
        CancellationToken cancellationToken = default);

    /// <summary>Approve: truyện (đúng bản đã ghim) vào thư viện cộng đồng; người nộp được báo.</summary>
    Task<CommunityReviewDecisionResultDto> ApproveAsync(
        int actorUserId, int itemId, CancellationToken cancellationToken = default);

    /// <summary>Reject với lý do bắt buộc; người nộp được báo và nộp lại được.</summary>
    Task<CommunityReviewDecisionResultDto> RejectAsync(
        int actorUserId, int itemId, RejectCommunityRequestDto request, CancellationToken cancellationToken = default);
}
