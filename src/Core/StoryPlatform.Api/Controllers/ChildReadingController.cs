using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Api.Extensions;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.ReadingSessions;
using StoryPlatform.Application.Features.ReadingSessions.Interfaces;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Api.Controllers;

/// <summary>Đọc và nghe truyện, phía trẻ (Luồng 3 — WF-L3A, WF-L3B).</summary>
[ApiController]
[Authorize(Policy = ServiceExtensions.ChildSessionPolicy)]
[Route("api/v1/child/reading")]
public sealed class ChildReadingController : ControllerBase
{
    private readonly IChildStoryBrowseService _browse;
    private readonly IChildReadingService _reading;
    private readonly IChildNotebookService _notebook;

    public ChildReadingController(
        IChildStoryBrowseService browse, IChildReadingService reading, IChildNotebookService notebook)
    {
        _browse = browse;
        _reading = reading;
        _notebook = notebook;
    }

    private int ChildId
    {
        get
        {
            var claim = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
            if (!int.TryParse(claim, out var id))
            {
                throw new UnauthorizedAccessException();
            }

            return id;
        }
    }

    private string SessionKey
    {
        get
        {
            var sessionKey = User.FindFirstValue(ChildSession.SessionClaimType);
            if (sessionKey == null)
            {
                throw new UnauthorizedAccessException();
            }

            return sessionKey;
        }
    }

    [HttpGet("stories")]
    public async Task<ActionResult<ApiResponse<ChildStoryPageDto>>> SearchStories(
        [FromQuery] ChildStoryQueryDto query, CancellationToken ct)
    {
        var result = await _browse.SearchAsync(ChildId, query, ct);
        return Ok(ApiResponse<ChildStoryPageDto>.Ok(result));
    }

    [HttpPost("sessions")]
    public async Task<ActionResult<ApiResponse<ReadingSessionStartedDto>>> Start(
        [FromBody] StartReadingRequestDto request, CancellationToken ct)
    {
        var result = await _reading.StartAsync(ChildId, SessionKey, request, ct);
        return Ok(ApiResponse<ReadingSessionStartedDto>.Ok(result));
    }

    [HttpGet("sessions/{sessionId:int}/content")]
    public async Task<ActionResult<ApiResponse<ReadingContentDto>>> GetContent(int sessionId, CancellationToken ct)
    {
        var result = await _reading.GetContentAsync(ChildId, sessionId, ct);
        return Ok(ApiResponse<ReadingContentDto>.Ok(result));
    }

    [HttpPut("sessions/{sessionId:int}/progress")]
    public async Task<ActionResult<ApiResponse<ReadingProgressDto>>> SaveProgress(
        int sessionId, [FromBody] SaveProgressRequestDto request, CancellationToken ct)
    {
        var result = await _reading.SaveProgressAsync(ChildId, sessionId, request, ct);
        return Ok(ApiResponse<ReadingProgressDto>.Ok(result));
    }

    [HttpPost("sessions/{sessionId:int}/complete")]
    public async Task<ActionResult<ApiResponse<ReadingCompletedDto>>> Complete(int sessionId, CancellationToken ct)
    {
        var result = await _reading.CompleteAsync(ChildId, sessionId, ct);
        return Ok(ApiResponse<ReadingCompletedDto>.Ok(result));
    }

    [HttpPut("sessions/{sessionId:int}/bookmark")]
    public async Task<ActionResult<ApiResponse<ReadingFlagsDto>>> SetBookmark(
        int sessionId, [FromBody] SetFlagRequestDto request, CancellationToken ct)
    {
        var result = await _reading.SetBookmarkAsync(ChildId, sessionId, request.Value, ct);
        return Ok(ApiResponse<ReadingFlagsDto>.Ok(result));
    }

    [HttpPut("sessions/{sessionId:int}/favorite")]
    public async Task<ActionResult<ApiResponse<ReadingFlagsDto>>> SetFavorite(
        int sessionId, [FromBody] SetFlagRequestDto request, CancellationToken ct)
    {
        var result = await _reading.SetFavoriteAsync(ChildId, sessionId, request.Value, ct);
        return Ok(ApiResponse<ReadingFlagsDto>.Ok(result));
    }

    /// <summary>Trẻ chọn cảm nhận sau khi đọc xong. Chỉ ghi lại, không có phản hồi hay thưởng.</summary>
    [HttpPut("sessions/{sessionId:int}/feeling")]
    public async Task<ActionResult<ApiResponse<ReadingFeelingDto>>> SetFeeling(
        int sessionId, [FromBody] SetFeelingRequestDto request, CancellationToken ct) =>
        Ok(ApiResponse<ReadingFeelingDto>.Ok(await _reading.SetFeelingAsync(ChildId, sessionId, request, ct)));

    /// <summary>App của trẻ gọi mỗi ≤10 giây để biết phiên có bị ngắt không (thông báo trong vòng 15 giây).</summary>
    [HttpGet("sessions/{sessionId:int}/signal")]
    public async Task<ActionResult<ApiResponse<ForceExitSignalDto>>> GetSignal(int sessionId, CancellationToken ct)
    {
        var result = await _reading.GetSignalAsync(ChildId, sessionId, ct);
        return Ok(ApiResponse<ForceExitSignalDto>.Ok(result));
    }

    [HttpPost("sessions/{sessionId:int}/events")]
    public async Task<ActionResult<ApiResponse<object?>>> LogEvent(
        int sessionId, [FromBody] ReadingEventRequestDto request, CancellationToken ct)
    {
        await _reading.LogEventAsync(ChildId, sessionId, request, ct);
        return Ok(ApiResponse<object?>.Ok(null, "Đã ghi nhận."));
    }

    [HttpGet("sessions/{sessionId:int}/vocabulary/{vocabularyId:int}")]
    public async Task<ActionResult<ApiResponse<VocabularyDefinitionDto>>> GetDefinition(
        int sessionId, int vocabularyId, CancellationToken ct)
    {
        var result = await _notebook.GetDefinitionAsync(ChildId, sessionId, vocabularyId, ct);
        return Ok(ApiResponse<VocabularyDefinitionDto>.Ok(result));
    }
}
