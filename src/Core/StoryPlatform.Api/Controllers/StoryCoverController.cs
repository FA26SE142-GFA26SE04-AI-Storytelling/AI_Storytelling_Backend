using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Application.Features.Stories.DTOs;
using StoryPlatform.Application.Features.Stories.Interfaces;

namespace StoryPlatform.Api.Controllers;

/// <summary>Ảnh bìa truyện: chọn một tranh minh họa của chính truyện kèm vùng hiển thị.</summary>
[Authorize(Roles = "User,ContentCreator")]
[Route("api/v1/stories/{storyId:int}/cover")]
public class StoryCoverController : BaseApiController
{
    private readonly IStoryCoverService _coverService;

    public StoryCoverController(IStoryCoverService coverService)
    {
        _coverService = coverService;
    }

    /// <summary>
    /// Chọn ảnh bìa từ tranh của truyện (tác giả Creator, hoặc phụ huynh chủ truyện); mediaAssetId = null để gỡ ảnh bìa.
    /// Truyện Creator đã công khai thì 409; ảnh không thuộc truyện hoặc vùng hiển thị sai thì 400.
    /// </summary>
    [HttpPut]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SetCover(
        int storyId, [FromBody] SetStoryCoverRequestDto request, CancellationToken cancellationToken)
    {
        await _coverService.SetCoverAsync(GetCurrentUserId(), storyId, request, cancellationToken);
        return NoContent();
    }
}
