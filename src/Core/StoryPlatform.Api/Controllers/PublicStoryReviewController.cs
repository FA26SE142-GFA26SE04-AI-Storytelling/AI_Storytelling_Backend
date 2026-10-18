using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.Moderation.DTOs;
using StoryPlatform.Application.Features.Moderation.Interfaces;

namespace StoryPlatform.Api.Controllers;

/// <summary>Lượt duyệt truyện Public Library của Moderator (WF-L5A, SCR-L5A-03).</summary>
[Authorize(Roles = "Moderator,Administrator")]
public class PublicStoryReviewController : BaseApiController
{
    private readonly IPublicStoryReviewService _reviewService;

    public PublicStoryReviewController(IPublicStoryReviewService reviewService)
    {
        _reviewService = reviewService;
    }

    /// <summary>Lấy gói nội dung của đúng phiên bản đang được duyệt cùng trạng thái lượt duyệt.</summary>
    /// <remarks>
    /// Lưu ý: người gọi phải là Moderator/Administrator đang hoạt động, đang giữ chính mục
    /// PublicStoryReview này và claim chưa quá 2 giờ. Mỗi lần mở gói đều được ghi audit.
    /// API chỉ trả dữ liệu của StoryVersion gắn với mục, không trả lịch sử phiên bản khác.
    /// </remarks>
    [HttpGet("{itemId:int}")]
    public async Task<ActionResult<ApiResponse<PublicReviewPackageDto>>> GetPackage(
        int itemId, CancellationToken cancellationToken)
    {
        var result = await _reviewService.GetPackageAsync(GetCurrentUserId(), itemId, cancellationToken);
        return HandleResult(result, "Lấy gói nội dung duyệt thành công.");
    }

    /// <summary>Đánh dấu một scene đã xem hoặc một đoạn audio đã nghe.</summary>
    /// <remarks>
    /// Lưu ý: PartType chỉ nhận "scene" hoặc "audio"; PartId phải thuộc đúng phiên bản đang duyệt.
    /// Audio chỉ được ghi nhận khi đã có file sẵn sàng. Thao tác này tăng RowVersion nhưng không ghi
    /// audit để tránh tạo quá nhiều bản ghi; gửi lại cùng một phần không tạo phần tử trùng.
    /// </remarks>
    [HttpPost("{itemId:int}/views")]
    public async Task<ActionResult<ApiResponse<PublicReviewProgressDto>>> MarkViewed(
        int itemId, [FromBody] MarkPartViewedRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _reviewService.MarkViewedAsync(GetCurrentUserId(), itemId, request, cancellationToken);
        return HandleResult(result, "Đã ghi nhận phần đã xem.");
    }

    /// <summary>Đánh dấu một cờ vùng xám là đã xem và an toàn.</summary>
    /// <remarks>
    /// Lưu ý: FlagId phải tồn tại trong GrayZoneFlags của phiên bản. Lý do là bắt buộc, được cắt
    /// khoảng trắng đầu/cuối và tối đa 1000 ký tự. Khi xử lý hết cờ, phiên bản được đánh dấu
    /// GrayZoneCleared. Thao tác hợp lệ tăng RowVersion và ghi audit; gọi lại cờ đã xử lý là idempotent.
    /// </remarks>
    [HttpPost("{itemId:int}/flags/resolve")]
    public async Task<ActionResult<ApiResponse<PublicReviewFlagsResultDto>>> ResolveFlag(
        int itemId, [FromBody] ResolveFlagRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _reviewService.ResolveFlagAsync(GetCurrentUserId(), itemId, request, cancellationToken);
        return HandleResult(result, "Đã xử lý cờ vùng xám.");
    }

    /// <summary>Lưu kết quả chấm checklist 8 tiêu chí của lượt duyệt.</summary>
    /// <remarks>
    /// Lưu ý: true = Đạt, false = Không đạt, null = giữ nguyên giá trị hiện tại. Phải gửi ít nhất
    /// một tiêu chí. Thao tác này chỉ lưu checklist, tăng RowVersion và ghi audit trạng thái
    /// trước/sau; quyết định cuối được thực hiện qua các endpoint riêng.
    /// </remarks>
    [HttpPut("{itemId:int}/checklist")]
    public async Task<ActionResult<ApiResponse<PublicReviewChecklistDto>>> SaveChecklist(
        int itemId, [FromBody] PublicReviewChecklistDto request, CancellationToken cancellationToken)
    {
        var result = await _reviewService.SaveChecklistAsync(GetCurrentUserId(), itemId, request, cancellationToken);
        return HandleResult(result, "Đã lưu checklist.");
    }

    /// <summary>Thay toàn bộ nhãn nội dung của truyện đang được duyệt.</summary>
    /// <remarks>
    /// Lưu ý: đây là ngoại lệ duy nhất cho phép Moderator sửa dữ liệu truyện trong lượt duyệt.
    /// Danh sách mới phải có ít nhất một nhãn đang hoạt động; ID trùng được tự loại bỏ và nhãn mới
    /// mang nguồn Moderator. Thao tác tăng RowVersion và ghi audit bộ nhãn trước/sau.
    /// </remarks>
    [HttpPut("{itemId:int}/labels")]
    public async Task<ActionResult<ApiResponse<List<PublicReviewLabelDto>>>> SetLabels(
        int itemId, [FromBody] SetContentLabelsRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _reviewService.SetContentLabelsAsync(
            GetCurrentUserId(), itemId, request, cancellationToken);
        return HandleResult(result, "Đã cập nhật nhãn nội dung.");
    }

    /// <summary>Approve phiên bản và công khai truyện ngay khi đủ mọi điều kiện.</summary>
    /// <remarks>
    /// Lưu ý: server luôn kiểm tra lại các điều kiện trong ApproveBlockers. Nội dung do chính người
    /// duyệt tạo trả 403; các điều kiện khác chưa đủ trả 400. Quyết định, audit và thông báo cho
    /// Content Creator được thực hiện trong cùng transaction và thao tác tăng RowVersion.
    /// </remarks>
    [HttpPost("{itemId:int}/approve")]
    public async Task<ActionResult<ApiResponse<PublicReviewDecisionResultDto>>> Approve(
        int itemId, CancellationToken cancellationToken)
    {
        var result = await _reviewService.ApproveAsync(GetCurrentUserId(), itemId, cancellationToken);
        return HandleResult(result, "Đã duyệt và công khai truyện.");
    }

    /// <summary>Yêu cầu Content Creator sửa các phần cụ thể của phiên bản.</summary>
    /// <remarks>
    /// Lưu ý: bắt buộc ghi chú chung và từ 1 đến 50 phần cần sửa. Mỗi phần phải có loại, vị trí
    /// hợp lệ thuộc đúng phiên bản và ghi chú tối đa 1000 ký tự. Quyết định không gỡ bản truyện
    /// đã công khai trước đó và được ghi audit, gửi thông báo trong cùng transaction.
    /// </remarks>
    [HttpPost("{itemId:int}/request-changes")]
    public async Task<ActionResult<ApiResponse<PublicReviewDecisionResultDto>>> RequestChanges(
        int itemId, [FromBody] RequestChangesDto request, CancellationToken cancellationToken)
    {
        var result = await _reviewService.RequestChangesAsync(
            GetCurrentUserId(), itemId, request, cancellationToken);
        return HandleResult(result, "Đã gửi yêu cầu sửa cho Content Creator.");
    }

    /// <summary>Từ chối phiên bản đang duyệt.</summary>
    /// <remarks>
    /// Lưu ý: lý do là bắt buộc và tối đa 1000 ký tự. Quyết định không thay đổi Story.Status và
    /// không gỡ bản truyện đã công khai trước đó; audit và thông báo được ghi trong cùng transaction.
    /// </remarks>
    [HttpPost("{itemId:int}/reject")]
    public async Task<ActionResult<ApiResponse<PublicReviewDecisionResultDto>>> Reject(
        int itemId, [FromBody] RejectRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _reviewService.RejectAsync(GetCurrentUserId(), itemId, request, cancellationToken);
        return HandleResult(result, "Đã từ chối truyện.");
    }
    /// <summary>So sánh bản đang duyệt với bản đã gửi trước hoặc bản đang công khai.</summary>
    /// <remarks>
    /// Truyền against=previous để so với phiên bản của lượt duyệt trước, hoặc against=public để so với
    /// phiên bản Approved mới nhất. Người gọi phải đang giữ mục và claim còn hiệu lực. Mỗi lượt so sánh
    /// đều được ghi audit; kết quả dùng diff theo câu, không dùng AI và không thay đổi nội dung truyện.
    /// </remarks>
    [HttpGet("{itemId:int}/compare")]
    public async Task<ActionResult<ApiResponse<PublicVersionComparisonDto>>> Compare(
        int itemId, [FromQuery] string? against, CancellationToken cancellationToken)
    {
        var result = await _reviewService.CompareAsync(GetCurrentUserId(), itemId, against, cancellationToken);
        return HandleResult(result, "So sánh phiên bản thành công.");
    }
}
