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
    /// một tiêu chí. API chưa thực hiện quyết định Approve/Request changes/Reject; thao tác này chỉ
    /// lưu checklist, tăng RowVersion và ghi audit trạng thái trước/sau.
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
}
