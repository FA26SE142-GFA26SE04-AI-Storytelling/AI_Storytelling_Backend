using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Api.Extensions;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.StoryWishes;
using StoryPlatform.Application.Features.StoryWishes.Interfaces;

namespace StoryPlatform.Api.Controllers;

/// <summary>Bé ước một câu chuyện, phía trẻ (WF-L2D UC-L2D-01).</summary>
[ApiController]
[Authorize(Policy = ServiceExtensions.ChildSessionPolicy)]
[Route("api/v1/child/wishes")]
public sealed class ChildWishesController : ControllerBase
{
    private readonly IStoryWishService _wishes;

    public ChildWishesController(IStoryWishService wishes)
    {
        _wishes = wishes;
    }

    private int ChildId => int.TryParse(
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var id)
        ? id : throw new UnauthorizedAccessException();

    /// <summary>Mở lại bản nháp còn hạn hoặc bắt đầu một điều ước mới.</summary>
    [HttpPost("draft")]
    public async Task<ActionResult<ApiResponse<WishDraftDto>>> StartDraft(CancellationToken ct) =>
        Ok(ApiResponse<WishDraftDto>.Ok(await _wishes.StartDraftAsync(ChildId, ct)));

    /// <summary>Chọn, đổi hoặc bỏ thẻ; bài học có thể để bố mẹ chọn.</summary>
    [HttpPut("draft/cards")]
    public async Task<ActionResult<ApiResponse<WishDraftDto>>> SelectCard(
        [FromBody] SelectWishCardRequestDto request, CancellationToken ct) =>
        Ok(ApiResponse<WishDraftDto>.Ok(await _wishes.SelectCardAsync(ChildId, request, ct)));

    /// <summary>Gửi bản nháp hoàn chỉnh cho bố mẹ.</summary>
    [HttpPost("draft/submit")]
    public async Task<ActionResult<ApiResponse<ChildWishDto>>> Submit(CancellationToken ct) =>
        Ok(ApiResponse<ChildWishDto>.Ok(
            await _wishes.SubmitAsync(ChildId, ct), StoryWishPolicy.SubmittedMessage));

    /// <summary>Lấy tối đa 10 điều ước gần nhất, không gồm bản nháp.</summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ChildWishDto>>>> ListMine(CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<ChildWishDto>>.Ok(await _wishes.ListMineAsync(ChildId, ct)));
}
