using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.WishCards;
using StoryPlatform.Application.Features.WishCards.Interfaces;

namespace StoryPlatform.Api.Controllers;

/// <summary>Moderator quản lý bộ thẻ ước cho Bé ước / Bé sáng tác (WF-L2D, WF-L5E). Ẩn chứ không xóa.</summary>
[Authorize(Roles = "Moderator,Administrator")]
[Route("api/v1/moderation/wish-cards")]
public sealed class WishCardManagementController : BaseApiController
{
    private readonly IWishCardManagementService _service;

    public WishCardManagementController(IWishCardManagementService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<WishCardDto>>>> List(
        [FromQuery] WishCardQueryDto query, CancellationToken ct)
    {
        var result = await _service.ListAsync(query, ct);
        return Ok(ApiResponse<PagedResult<WishCardDto>>.Ok(result));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ApiResponse<WishCardDto>>> Get(int id, CancellationToken ct)
    {
        var result = await _service.GetAsync(id, ct);
        return Ok(ApiResponse<WishCardDto>.Ok(result));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<WishCardDto>>> Create(
        [FromBody] CreateWishCardRequestDto request, CancellationToken ct)
    {
        var result = await _service.CreateAsync(GetCurrentUserId(), request, ct);
        return StatusCode(201, ApiResponse<WishCardDto>.Ok(result, "Đã tạo thẻ ước."));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<ApiResponse<WishCardDto>>> Update(
        int id, [FromBody] UpdateWishCardRequestDto request, CancellationToken ct)
    {
        var result = await _service.UpdateAsync(GetCurrentUserId(), id, request, ct);
        return Ok(ApiResponse<WishCardDto>.Ok(result, "Đã cập nhật thẻ ước."));
    }

    /// <summary>Ẩn hoặc hiện lại thẻ. Ẩn chỉ bỏ thẻ khỏi lựa chọn mới, không xóa dữ liệu cũ.</summary>
    [HttpPut("{id:int}/active")]
    public async Task<ActionResult<ApiResponse<WishCardDto>>> SetActive(
        int id, [FromBody] SetWishCardActiveRequestDto request, CancellationToken ct)
    {
        var result = await _service.SetActiveAsync(GetCurrentUserId(), id, request.IsActive, ct);
        return Ok(ApiResponse<WishCardDto>.Ok(result, "Đã cập nhật trạng thái thẻ."));
    }
}
