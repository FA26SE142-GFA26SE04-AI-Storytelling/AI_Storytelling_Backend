using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.CommunityLibrary;
using StoryPlatform.Application.Features.CommunityLibrary.Interfaces;

namespace StoryPlatform.Api.Controllers;

/// <summary>Lượt duyệt truyện Community của Moderator (Luồng 4 — WF-L4F UC-L4F-02, SCR-L4F-02).</summary>
[Authorize(Roles = "Moderator,Administrator")]
public class CommunityReviewController : BaseApiController
{
    private readonly ICommunityReviewService _reviewService;

    public CommunityReviewController(ICommunityReviewService reviewService)
    {
        _reviewService = reviewService;
    }

    /// <summary>Mở gói duyệt: đúng bản đã ghim, nhãn, kết quả dò PII, checklist và các khóa Approve.</summary>
    /// <remarks>
    /// Lưu ý: người gọi phải đang giữ chính mục community_submission này và lượt nhận chưa quá 2 giờ. Văn bản đã che
    /// số điện thoại, email và biệt danh của bé nguồn. Mỗi lần mở được ghi audit.
    /// </remarks>
    [HttpGet("{itemId:int}")]
    public async Task<ActionResult<ApiResponse<CommunityReviewPackageDto>>> GetPackage(
        int itemId, CancellationToken cancellationToken)
    {
        var result = await _reviewService.GetPackageAsync(GetCurrentUserId(), itemId, cancellationToken);
        return HandleResult(result, "Lấy gói duyệt Community thành công.");
    }

    /// <summary>Lưu (một phần) checklist 3 tiêu chí: safety_baseline, no_personal_info, metadata_labels.</summary>
    [HttpPut("{itemId:int}/checklist")]
    public async Task<ActionResult<ApiResponse<List<CommunityChecklistEntryDto>>>> SaveChecklist(
        int itemId, [FromBody] SaveCommunityChecklistRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _reviewService.SaveChecklistAsync(GetCurrentUserId(), itemId, request, cancellationToken);
        return HandleResult(result, "Đã lưu checklist.");
    }

    /// <summary>Sửa nhãn nội dung của truyện (ít nhất một nhãn, nhãn còn hoạt động).</summary>
    [HttpPut("{itemId:int}/labels")]
    public async Task<ActionResult<ApiResponse<List<CommunityLabelDto>>>> SetLabels(
        int itemId, [FromBody] SetCommunityLabelsRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _reviewService.SetLabelsAsync(GetCurrentUserId(), itemId, request, cancellationToken);
        return HandleResult(result, "Đã cập nhật nhãn nội dung.");
    }

    /// <summary>Approve: truyện (đúng bản đã ghim) vào thư viện cộng đồng.</summary>
    /// <remarks>
    /// Lưu ý: bị khóa (400) khi checklist chưa đạt đủ 3 tiêu chí, dò lại vẫn thấy thông tin cá nhân, truyện gốc đã
    /// lưu trữ, chưa có nhãn hoặc người duyệt là người nộp; khi đó chọn Reject.
    /// </remarks>
    [HttpPost("{itemId:int}/approve")]
    public async Task<ActionResult<ApiResponse<CommunityReviewDecisionResultDto>>> Approve(
        int itemId, CancellationToken cancellationToken)
    {
        var result = await _reviewService.ApproveAsync(GetCurrentUserId(), itemId, cancellationToken);
        return HandleResult(result, "Đã duyệt truyện lên thư viện cộng đồng.");
    }

    /// <summary>Reject với lý do bắt buộc; người nộp được báo và nộp lại được.</summary>
    [HttpPost("{itemId:int}/reject")]
    public async Task<ActionResult<ApiResponse<CommunityReviewDecisionResultDto>>> Reject(
        int itemId, [FromBody] RejectCommunityRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _reviewService.RejectAsync(GetCurrentUserId(), itemId, request, cancellationToken);
        return HandleResult(result, "Đã từ chối truyện Community.");
    }
}
