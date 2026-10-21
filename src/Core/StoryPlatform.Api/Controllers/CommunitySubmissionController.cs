using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.CommunityLibrary;
using StoryPlatform.Application.Features.CommunityLibrary.Interfaces;

namespace StoryPlatform.Api.Controllers;

/// <summary>Nộp, rút và gỡ truyện Community, phía phụ huynh (Luồng 4 — WF-L4F).</summary>
[Authorize(Roles = "Parent")]
public class CommunitySubmissionController : BaseApiController
{
    private readonly ICommunitySubmissionService _submissionService;

    public CommunitySubmissionController(ICommunitySubmissionService submissionService)
    {
        _submissionService = submissionService;
    }

    /// <summary>Kiểm tra truyện có nộp Community được không và vì sao (không thay đổi dữ liệu).</summary>
    /// <remarks>
    /// Lưu ý: cần quyền duyệt truyện với bé nguồn. Trả danh sách lý do (blockers) và các chỗ nghi chứa thông tin cá
    /// nhân (piiFindings) kể cả khi canSubmit = false. Truyện tự viết / nhập cần khai copyrightDeclaration = OwnWork.
    /// </remarks>
    [HttpPost("check")]
    public async Task<ActionResult<ApiResponse<CommunityEligibilityDto>>> Check(
        [FromBody] CommunitySubmitRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _submissionService.CheckAsync(GetCurrentUserId(), request, cancellationToken);
        return HandleResult(result, "Đã kiểm tra điều kiện nộp Community.");
    }

    /// <summary>Nộp truyện: ghim phiên bản hiện hành và đưa vào hàng đợi cho Moderator duyệt.</summary>
    /// <remarks>
    /// Lưu ý: confirmNoPersonalInfo phải là true. 400 kèm lý do khi chưa đủ điều kiện hoặc còn thông tin cá nhân;
    /// 409 khi truyện đã được nộp; 403 khi thiếu quyền duyệt truyện với bé nguồn.
    /// </remarks>
    [HttpPost]
    public async Task<ActionResult<ApiResponse<CommunitySubmitResultDto>>> Submit(
        [FromBody] CommunitySubmitRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _submissionService.SubmitAsync(GetCurrentUserId(), request, cancellationToken);
        return HandleResult(result, "Đã nộp truyện lên Community, đang chờ duyệt.");
    }

    /// <summary>Rút lần nộp khi chưa Moderator nào nhận.</summary>
    [HttpPost("{itemId:int}/withdraw")]
    public async Task<ActionResult<ApiResponse<CommunitySubmissionDto>>> Withdraw(
        int itemId, CancellationToken cancellationToken)
    {
        var result = await _submissionService.WithdrawAsync(GetCurrentUserId(), itemId, cancellationToken);
        return HandleResult(result, "Đã rút lần nộp.");
    }

    /// <summary>Gỡ truyện đã được duyệt khỏi thư viện cộng đồng.</summary>
    [HttpPost("{itemId:int}/remove")]
    public async Task<ActionResult<ApiResponse<CommunitySubmissionDto>>> Remove(
        int itemId, CancellationToken cancellationToken)
    {
        var result = await _submissionService.RemoveAsync(GetCurrentUserId(), itemId, cancellationToken);
        return HandleResult(result, "Đã gỡ truyện khỏi thư viện cộng đồng.");
    }

    /// <summary>Các lần nộp của tôi và trạng thái (không nêu người duyệt).</summary>
    [HttpGet("mine")]
    public async Task<ActionResult<ApiResponse<List<CommunitySubmissionDto>>>> ListMine(
        CancellationToken cancellationToken)
    {
        var result = await _submissionService.ListMineAsync(GetCurrentUserId(), cancellationToken);
        return HandleResult(result, "Lấy danh sách lần nộp thành công.");
    }
}
