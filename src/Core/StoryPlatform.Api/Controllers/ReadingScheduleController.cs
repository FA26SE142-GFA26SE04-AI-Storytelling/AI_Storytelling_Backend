using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Api.Authorization;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.AccessControl;
using StoryPlatform.Application.Features.ChildProfiles.ReadingTime.DTOs;
using StoryPlatform.Application.Features.ChildProfiles.ReadingTime.Interfaces;

namespace StoryPlatform.Api.Controllers;

/// <summary>Cấu hình "Thời gian &amp; thói quen" của hồ sơ trẻ (khung giờ, giờ ngủ, nghỉ mắt, giới hạn màn hình) — chỉ chủ hồ sơ.</summary>
public class ReadingScheduleController : BaseApiController
{
    private readonly IReadingScheduleService _readingScheduleService;

    public ReadingScheduleController(IReadingScheduleService readingScheduleService)
    {
        _readingScheduleService = readingScheduleService;
    }

    /// <summary>Cấu hình hiện tại; chưa lưu lần nào thì trả gợi ý theo độ tuổi (isSuggested = true).</summary>
    [HttpGet("{childProfileId:int}")]
    [HasPermission(Permissions.Parent.ChildProfileManage)]
    public async Task<ActionResult<ApiResponse<ReadingScheduleDto>>> GetReadingSchedule(
        int childProfileId, CancellationToken cancellationToken)
    {
        var result = await _readingScheduleService.GetAsync(childProfileId, GetCurrentUserId(), cancellationToken);
        return HandleResult(result, "Lấy cấu hình thời gian đọc thành công.");
    }

    /// <summary>Lưu toàn bộ cấu hình (thay danh sách khung giờ); có hiệu lực ngay từ lần kiểm tra kế tiếp.</summary>
    [HttpPut("{childProfileId:int}")]
    [HasPermission(Permissions.Parent.ChildProfileManage)]
    public async Task<ActionResult<ApiResponse<ReadingScheduleDto>>> SetReadingSchedule(
        int childProfileId,
        [FromBody] SetReadingScheduleRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await _readingScheduleService.SetAsync(
            childProfileId, GetCurrentUserId(), request, cancellationToken);
        return HandleResult(result, "Lưu cấu hình thời gian đọc thành công.");
    }
}
