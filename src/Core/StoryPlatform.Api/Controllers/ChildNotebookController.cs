using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Api.Extensions;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.ReadingSessions;
using StoryPlatform.Application.Features.ReadingSessions.Interfaces;

namespace StoryPlatform.Api.Controllers;

/// <summary>Sổ tay của em (từ đã lưu), phía trẻ.</summary>
[ApiController]
[Authorize(Policy = ServiceExtensions.ChildSessionPolicy)]
[Route("api/v1/child/notebook")]
public sealed class ChildNotebookController : ControllerBase
{
    private readonly IChildNotebookService _notebook;

    public ChildNotebookController(IChildNotebookService notebook)
    {
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

    [HttpPost("sessions/{sessionId:int}/words")]
    public async Task<ActionResult<ApiResponse<NotebookEntryDto>>> Collect(
        int sessionId, [FromBody] CollectVocabularyRequestDto request, CancellationToken ct)
    {
        var result = await _notebook.CollectAsync(ChildId, sessionId, request.VocabularyId, ct);
        return Ok(ApiResponse<NotebookEntryDto>.Ok(result, "Đã lưu vào Sổ tay."));
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<NotebookEntryDto>>>> List(CancellationToken ct)
    {
        var result = await _notebook.ListAsync(ChildId, ct);
        return Ok(ApiResponse<IReadOnlyList<NotebookEntryDto>>.Ok(result));
    }

    [HttpDelete("{entryId:int}")]
    public async Task<ActionResult<ApiResponse<object?>>> Remove(int entryId, CancellationToken ct)
    {
        await _notebook.RemoveAsync(ChildId, entryId, ct);
        return Ok(ApiResponse<object?>.Ok(null, "Đã xóa khỏi Sổ tay."));
    }
}
