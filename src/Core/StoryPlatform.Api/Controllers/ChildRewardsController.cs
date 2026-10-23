using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Api.Extensions;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.ReadingSessions;
using StoryPlatform.Application.Features.ReadingSessions.Interfaces;

namespace StoryPlatform.Api.Controllers;

/// <summary>Thành tích của em: EXP, streak, huy hiệu (Luồng 3 — mục 8).</summary>
[ApiController]
[Authorize(Policy = ServiceExtensions.ChildSessionPolicy)]
[Route("api/v1/child/rewards")]
public sealed class ChildRewardsController : ControllerBase
{
    private readonly IChildRewardService _rewards;

    public ChildRewardsController(IChildRewardService rewards)
    {
        _rewards = rewards;
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

    [HttpGet]
    public async Task<ActionResult<ApiResponse<ChildRewardsDto>>> Get(CancellationToken ct)
    {
        var result = await _rewards.GetSummaryAsync(ChildId, ct);
        return Ok(ApiResponse<ChildRewardsDto>.Ok(result));
    }
}
