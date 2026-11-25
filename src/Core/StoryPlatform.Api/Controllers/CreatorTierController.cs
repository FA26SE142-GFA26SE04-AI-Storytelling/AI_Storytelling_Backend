using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Api.Extensions;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.CreatorTiers.DTOs;
using StoryPlatform.Application.Features.CreatorTiers.Interfaces;
using StoryPlatform.Api.Authorization;
using StoryPlatform.Application.Features.AccessControl;

namespace StoryPlatform.Api.Controllers;

/// <summary>
/// Administrator xem và thu hồi cấp Creator đối tác (Luồng 5, 5g). Việc xét cấp hằng tháng chạy tự động
/// (SubscriptionCycleWorker và lúc cấp hạn mức tháng); Creator xem tiến độ của mình ở CreatorStatistics/content.
/// </summary>
[HasPermission(Permissions.Admin.CreatorTierManage)]
[Route("api/v1/admin/creators/{creatorUserId:int}/partner-tier")]
public class CreatorTierController : BaseApiController
{
    private readonly ICreatorTierService _service;

    public CreatorTierController(ICreatorTierService service)
    {
        _service = service;
    }

    /// <summary>Cấp hiện tại, tiến độ 4 tiêu chí (cửa sổ 90 ngày đến hôm nay) và lịch sử xét 12 tháng gần nhất.</summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<CreatorTierOverviewDto>>> Get(int creatorUserId, CancellationToken cancellationToken)
    {
        var result = await _service.GetOverviewAsync(creatorUserId, cancellationToken);
        return HandleResult(result, "Lấy cấp nhà sáng tạo đối tác thành công.");
    }

    /// <summary>Thu hồi cấp đối tác, lý do bắt buộc; Creator về cấp thường ngay, lần xét tháng sau vẫn xét bình thường.</summary>
    [HttpPost("revoke")]
    public async Task<ActionResult<ApiResponse<CreatorTierOverviewDto>>> Revoke(
        int creatorUserId, [FromBody] RevokePartnerTierRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _service.RevokeAsync(GetCurrentUserId(), creatorUserId, request, cancellationToken);
        return HandleResult(result, "Đã thu hồi cấp nhà sáng tạo đối tác.");
    }
}
