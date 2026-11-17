using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.ReadingSessions;
using StoryPlatform.Application.Features.ReadingSessions.Interfaces;

namespace StoryPlatform.Api.Controllers;

/// <summary>Con đã đọc gì và cảm nhận ra sao, phía phụ huynh (Luồng 3 — mục 8).</summary>
[Authorize(Roles = "User")]
public class ParentReadingFeedbackController : BaseApiController
{
    private readonly IParentReadingFeedbackService _feedback;
    private readonly IParentReadAloudRecordingService _readAloud;

    public ParentReadingFeedbackController(IParentReadingFeedbackService feedback, IParentReadAloudRecordingService readAloud)
    {
        _feedback = feedback;
        _readAloud = readAloud;
    }

    [HttpGet("{childProfileId:int}")]
    public async Task<ActionResult<ApiResponse<PagedResult<ParentReadingFeedbackItemDto>>>> List(
        int childProfileId, [FromQuery] ParentReadingFeedbackQueryDto query, CancellationToken cancellationToken)
    {
        var result = await _feedback.ListAsync(GetCurrentUserId(), childProfileId, query, cancellationToken);
        return HandleResult(result, "Lấy danh sách truyện con đã đọc thành công.");
    }

    /// <summary>Chi tiết câu đố của truyện trong phiên đọc: các lần làm và câu trả lời gần nhất từng câu. Chỉ chủ hồ sơ.</summary>
    [HttpGet("{childProfileId:int}/sessions/{sessionId:int}/quiz")]
    public async Task<ActionResult<ApiResponse<ParentQuizDetailDto>>> Quiz(
        int childProfileId, int sessionId, CancellationToken cancellationToken)
    {
        var result = await _feedback.GetQuizAsync(GetCurrentUserId(), childProfileId, sessionId, cancellationToken);
        return HandleResult(result, "Lấy chi tiết câu đố của con thành công.");
    }

    /// <summary>Số liệu tổng quan việc đọc của con trong khoảng [From, To) (mặc định: mọi thời điểm). Chỉ chủ hồ sơ.</summary>
    [HttpGet("{childProfileId:int}/overview")]
    public async Task<ActionResult<ApiResponse<ParentReadingOverviewDto>>> Overview(
        int childProfileId, [FromQuery] ParentReadingOverviewQueryDto query, CancellationToken cancellationToken)
    {
        var result = await _feedback.GetOverviewAsync(GetCurrentUserId(), childProfileId, query, cancellationToken);
        return HandleResult(result, "Lấy tổng quan việc đọc của con thành công.");
    }
    /// <summary>Bản ghi đọc to (lần gần nhất mỗi đoạn) của con trong một phiên, kèm chữ đoạn, điểm và link nghe 15 phút. Chỉ chủ hồ sơ.</summary>
    [HttpGet("{childProfileId:int}/sessions/{sessionId:int}/read-aloud")]
    public async Task<ActionResult<ApiResponse<ParentReadAloudRecordingsDto>>> ReadAloudRecordings(
        int childProfileId, int sessionId, CancellationToken cancellationToken)
    {
        var result = await _readAloud.ListAsync(GetCurrentUserId(), childProfileId, sessionId, cancellationToken);
        return HandleResult(result, "Lấy bản ghi đọc to của con thành công.");
    }

    /// <summary>Xóa một bản ghi đọc to khỏi kho lưu trữ. Chỉ chủ hồ sơ.</summary>
    [HttpDelete("{childProfileId:int}/sessions/{sessionId:int}/read-aloud/{recordingId:int}")]
    public async Task<ActionResult<ApiResponse<object?>>> DeleteReadAloudRecording(
        int childProfileId, int sessionId, int recordingId, CancellationToken cancellationToken)
    {
        await _readAloud.DeleteAsync(GetCurrentUserId(), childProfileId, sessionId, recordingId, cancellationToken);
        return HandleResult<object?>(null, "Đã xóa bản ghi đọc to.");
    }
}
