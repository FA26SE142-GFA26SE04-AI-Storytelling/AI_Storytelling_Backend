using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.ReadingSessions;
using StoryPlatform.Application.Features.ReadingSessions.Interfaces;

namespace StoryPlatform.Api.Controllers;

/// <summary>Tóm tắt cuối truyện con gửi, phía chủ hồ sơ: nghe ghi âm, xem chỗ con thắc mắc, xóa.</summary>
[Authorize(Roles = "User")]
public class ParentStorySummaryController : BaseApiController
{
    private readonly IParentStorySummaryService _summaries;

    public ParentStorySummaryController(IParentStorySummaryService summaries)
    {
        _summaries = summaries;
    }

    /// <summary>Danh sách bản tóm tắt của con; recordingUrl hết hạn sau 15 phút. Chỉ chủ hồ sơ.</summary>
    [HttpGet("{childProfileId:int}")]
    public async Task<ActionResult<ApiResponse<PagedResult<ParentStorySummaryItemDto>>>> List(
        int childProfileId, [FromQuery] ParentStorySummaryQueryDto query, CancellationToken cancellationToken)
    {
        var result = await _summaries.ListAsync(GetCurrentUserId(), childProfileId, query, cancellationToken);
        return HandleResult(result, "Lấy danh sách tóm tắt của con thành công.");
    }

    /// <summary>Xóa bản tóm tắt: xóa file ghi âm khỏi kho lưu trữ và ẩn bản tóm tắt. Chỉ chủ hồ sơ.</summary>
    [HttpDelete("{childProfileId:int}/{summaryId:int}")]
    public async Task<ActionResult<ApiResponse<object?>>> Delete(
        int childProfileId, int summaryId, CancellationToken cancellationToken)
    {
        await _summaries.DeleteAsync(GetCurrentUserId(), childProfileId, summaryId, cancellationToken);
        return HandleResult<object?>(null, "Đã xóa bản tóm tắt.");
    }
}
