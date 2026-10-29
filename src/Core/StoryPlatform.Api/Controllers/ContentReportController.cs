using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.ContentReports.DTOs;
using StoryPlatform.Application.Features.ContentReports.Interfaces;

namespace StoryPlatform.Api.Controllers;

/// <summary>Người giám sát báo cáo truyện đã công khai (WF-L4E, UC-L4E-01).</summary>
[Authorize(Roles = "User")]
public class ContentReportController : BaseApiController
{
    private readonly IContentReportService _reportService;

    public ContentReportController(IContentReportService reportService)
    {
        _reportService = reportService;
    }

    /// <summary>Báo cáo một truyện mà con của người gọi truy cập được.</summary>
    /// <remarks>
    /// Báo cáo được tạo ở trạng thái Pending và đưa vào hàng đợi Moderator. SafetyConcern có ưu tiên High;
    /// các lý do khác có ưu tiên Medium. Thao tác này không tự ẩn hoặc Archive truyện. Reason phải gửi dưới
    /// dạng chuỗi enum; AttachmentUrl, nếu có, phải là URL http/https tuyệt đối.
    /// </remarks>
    [HttpPost]
    public async Task<ActionResult<ApiResponse<ContentReportResultDto>>> Create(
        [FromBody] CreateContentReportRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _reportService.CreateAsync(GetCurrentUserId(), request, cancellationToken);
        return HandleResult(result, "Đã gửi báo cáo. Cảm ơn bạn đã giúp giữ an toàn cho trẻ.");
    }
}
