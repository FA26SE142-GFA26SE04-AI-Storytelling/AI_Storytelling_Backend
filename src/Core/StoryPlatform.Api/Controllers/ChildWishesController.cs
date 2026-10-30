using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Api.Extensions;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.StoryWishes;
using StoryPlatform.Application.Features.StoryWishes.Interfaces;
using StoryPlatform.Application.Features.WishCards;
using StoryPlatform.Application.Features.WishCards.Interfaces;

namespace StoryPlatform.Api.Controllers;

/// <summary>Bé ước một câu chuyện, phía trẻ (WF-L2D UC-L2D-01).</summary>
[ApiController]
[Authorize(Policy = ServiceExtensions.ChildSessionPolicy)]
[Route("api/v1/child/wishes")]
public sealed class ChildWishesController : ControllerBase
{
    private readonly IStoryWishService _wishes;
    private readonly IChildWishCardService _cards;

    public ChildWishesController(IStoryWishService wishes, IChildWishCardService cards)
    {
        _wishes = wishes;
        _cards = cards;
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

    /// <summary>Tối đa 8 thẻ của một nhóm để bé chọn: Character, Place, Challenge hoặc Lesson.</summary>
    [HttpGet("/api/v1/child/wish-cards")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ChildWishCardDto>>>> ListCards(
        [FromQuery] string? group, CancellationToken ct)
    {
        var result = await _cards.ListAsync(ChildId, group, ct);
        return Ok(ApiResponse<IReadOnlyList<ChildWishCardDto>>.Ok(result));
    }

    /// <summary>Mở lại bản nháp còn hạn hoặc bắt đầu một điều ước mới.</summary>
    [HttpPost("draft")]
    public async Task<ActionResult<ApiResponse<WishDraftDto>>> StartDraft(CancellationToken ct)
    {
        var result = await _wishes.StartDraftAsync(ChildId, ct);
        return Ok(ApiResponse<WishDraftDto>.Ok(result));
    }

    /// <summary>Chọn, đổi hoặc bỏ thẻ; bài học có thể để bố mẹ chọn.</summary>
    [HttpPut("draft/cards")]
    public async Task<ActionResult<ApiResponse<WishDraftDto>>> SelectCard(
        [FromBody] SelectWishCardRequestDto request, CancellationToken ct)
    {
        var result = await _wishes.SelectCardAsync(ChildId, request, ct);
        return Ok(ApiResponse<WishDraftDto>.Ok(result));
    }

    /// <summary>Gửi bản nháp hoàn chỉnh cho bố mẹ.</summary>
    [HttpPost("draft/submit")]
    public async Task<ActionResult<ApiResponse<ChildWishDto>>> Submit(CancellationToken ct)
    {
        var result = await _wishes.SubmitAsync(ChildId, ct);
        return Ok(ApiResponse<ChildWishDto>.Ok(result, StoryWishPolicy.SubmittedMessage));
    }

    /// <summary>Lấy tối đa 10 điều ước gần nhất, không gồm bản nháp.</summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ChildWishDto>>>> ListMine(CancellationToken ct)
    {
        var result = await _wishes.ListMineAsync(ChildId, ct);
        return Ok(ApiResponse<IReadOnlyList<ChildWishDto>>.Ok(result));
    }
}
