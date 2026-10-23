using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.ReadingSessions;
using StoryPlatform.Application.Features.ReadingSessions.Interfaces;

namespace StoryPlatform.Api.Controllers;

/// <summary>Con đã đọc gì và cảm nhận ra sao, phía phụ huynh (Luồng 3 — mục 8).</summary>
[Authorize(Roles = "Parent")]
public class ParentReadingFeedbackController : BaseApiController
{
    private readonly IParentReadingFeedbackService _feedback;

    public ParentReadingFeedbackController(IParentReadingFeedbackService feedback)
    {
        _feedback = feedback;
    }

    [HttpGet("{childProfileId:int}")]
    public async Task<ActionResult<ApiResponse<PagedResult<ParentReadingFeedbackItemDto>>>> List(
        int childProfileId, [FromQuery] ParentReadingFeedbackQueryDto query, CancellationToken cancellationToken)
    {
        var result = await _feedback.ListAsync(GetCurrentUserId(), childProfileId, query, cancellationToken);
        return HandleResult(result, "Lấy danh sách truyện con đã đọc thành công.");
    }
}
