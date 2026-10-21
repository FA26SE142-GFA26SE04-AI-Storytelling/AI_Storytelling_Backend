using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.ChildRoadmaps;
using StoryPlatform.Application.Features.ChildRoadmaps.Interfaces;

namespace StoryPlatform.Api.Controllers;

/// <summary>Mục "Bố mẹ chọn cho em" trên Journey Map, phía trẻ.</summary>
[Authorize(Policy = "ChildSession")]
public class MyRoadmapController : BaseApiController
{
    private readonly IMyRoadmapService _myRoadmapService;

    public MyRoadmapController(IMyRoadmapService myRoadmapService)
    {
        _myRoadmapService = myRoadmapService;
    }

    /// <summary>Lộ trình của chính bé với truyện đang mở, sắp tới và đã đọc xong.</summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<MyRoadmapDto>>> Get(CancellationToken cancellationToken)
    {
        var result = await _myRoadmapService.GetAsync(GetCurrentUserId(), cancellationToken);
        return HandleResult(result, "Lấy mục Bố mẹ chọn cho em thành công.");
    }
}
