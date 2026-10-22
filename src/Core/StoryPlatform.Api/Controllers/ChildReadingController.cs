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

    public ChildReadingController(IChildStoryBrowseService browse, IChildReadingService reading)
    {
        _browse = browse;
        _reading = reading;
    }

    private int ChildId => int.TryParse(
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var id)
        ? id : throw new UnauthorizedAccessException();

    private string SessionKey => User.FindFirstValue(ChildSession.SessionClaimType)
                                 ?? throw new UnauthorizedAccessException();

    [HttpGet("stories")]
    public async Task<ActionResult<ApiResponse<ChildStoryPageDto>>> SearchStories(
        [FromQuery] ChildStoryQueryDto query, CancellationToken ct) =>
        Ok(ApiResponse<ChildStoryPageDto>.Ok(await _browse.SearchAsync(ChildId, query, ct)));

    [HttpPost("sessions")]
    public async Task<ActionResult<ApiResponse<ReadingSessionStartedDto>>> Start(
        [FromBody] StartReadingRequestDto request, CancellationToken ct) =>
        Ok(ApiResponse<ReadingSessionStartedDto>.Ok(await _reading.StartAsync(ChildId, SessionKey, request, ct)));

    [HttpGet("sessions/{sessionId:int}/content")]
    public async Task<ActionResult<ApiResponse<ReadingContentDto>>> GetContent(int sessionId, CancellationToken ct) =>
        Ok(ApiResponse<ReadingContentDto>.Ok(await _reading.GetContentAsync(ChildId, sessionId, ct)));

    [HttpPut("sessions/{sessionId:int}/progress")]
    public async Task<ActionResult<ApiResponse<ReadingProgressDto>>> SaveProgress(
        int sessionId, [FromBody] SaveProgressRequestDto request, CancellationToken ct) =>
        Ok(ApiResponse<ReadingProgressDto>.Ok(await _reading.SaveProgressAsync(ChildId, sessionId, request, ct)));

    [HttpPost("sessions/{sessionId:int}/complete")]
    public async Task<ActionResult<ApiResponse<ReadingCompletedDto>>> Complete(int sessionId, CancellationToken ct) =>
        Ok(ApiResponse<ReadingCompletedDto>.Ok(await _reading.CompleteAsync(ChildId, sessionId, ct)));

    [HttpPut("sessions/{sessionId:int}/bookmark")]
    public async Task<ActionResult<ApiResponse<ReadingFlagsDto>>> SetBookmark(
        int sessionId, [FromBody] SetFlagRequestDto request, CancellationToken ct) =>
        Ok(ApiResponse<ReadingFlagsDto>.Ok(await _reading.SetBookmarkAsync(ChildId, sessionId, request.Value, ct)));

    [HttpPut("sessions/{sessionId:int}/favorite")]
    public async Task<ActionResult<ApiResponse<ReadingFlagsDto>>> SetFavorite(
        int sessionId, [FromBody] SetFlagRequestDto request, CancellationToken ct) =>
        Ok(ApiResponse<ReadingFlagsDto>.Ok(await _reading.SetFavoriteAsync(ChildId, sessionId, request.Value, ct)));
}
