using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.ReadingSessions;
using StoryPlatform.Application.Features.ReadingSessions.Interfaces;
using StoryPlatform.Api.Authorization;
using StoryPlatform.Application.Features.AccessControl;

namespace StoryPlatform.Api.Controllers;

/// <summary>Tóm tắt cuối truyện con gửi, phía chủ hồ sơ: nghe ghi âm, xem câu hỏi theo tranh/cảnh, đánh dấu đã trả lời và xóa.</summary>
[HasPermission(Permissions.Parent.FeedbackManage)]
public class ParentStorySummaryController : BaseApiController
{
    private readonly IParentStorySummaryService _summaries;

    public ParentStorySummaryController(IParentStorySummaryService summaries)
    {
        _summaries = summaries;
    }

    /// <summary>Danh sách bản tóm tắt của con: ghi âm kể lại và các câu bé hỏi (tranh, cảnh, nội dung cảnh); link ghi âm hết hạn sau 15 phút. Chỉ chủ hồ sơ.</summary>
    [HttpGet("{childProfileId:int}")]
    public async Task<ActionResult<ApiResponse<PagedResult<ParentStorySummaryItemDto>>>> List(
        int childProfileId, [FromQuery] ParentStorySummaryQueryDto query, CancellationToken cancellationToken)
    {
        var result = await _summaries.ListAsync(GetCurrentUserId(), childProfileId, query, cancellationToken);
        return HandleResult(result, "Lấy danh sách tóm tắt của con thành công.");
    }

    /// <summary>Xóa bản tóm tắt: xóa ghi âm kể lại/câu hỏi khỏi kho lưu trữ và ẩn bản tóm tắt. Chỉ chủ hồ sơ.</summary>
    [HttpDelete("{childProfileId:int}/{summaryId:int}")]
    public async Task<ActionResult<ApiResponse<object?>>> Delete(
        int childProfileId, int summaryId, CancellationToken cancellationToken)
    {
        await _summaries.DeleteAsync(GetCurrentUserId(), childProfileId, summaryId, cancellationToken);
        return HandleResult<object?>(null, "Đã xóa bản tóm tắt.");
    }

    /// <summary>Đánh dấu hoặc bỏ đánh dấu đã trả lời một câu bé hỏi. Chỉ chủ hồ sơ.</summary>
    [HttpPut("{childProfileId:int}/{summaryId:int}/questions/{questionId:int}/answered")]
    public async Task<ActionResult<ApiResponse<StorySummaryAnsweredDto>>> SetAnswered(
        int childProfileId, int summaryId, int questionId, [FromBody] StorySummaryAnsweredRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await _summaries.SetAnsweredAsync(GetCurrentUserId(), childProfileId, summaryId, questionId,
            request.Answered, cancellationToken);
        return HandleResult(result, request.Answered ? "Đã đánh dấu đã trả lời." : "Đã bỏ đánh dấu đã trả lời.");
    }
}
