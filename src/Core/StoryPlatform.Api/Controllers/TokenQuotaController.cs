using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.TokenQuota.DTOs;
using StoryPlatform.Application.Features.TokenQuota.Interfaces;

namespace StoryPlatform.Api.Controllers;

/// <summary>
/// Cấu hình và tra cứu Token Quota (Bước 5.1c).
/// </summary>
public class TokenQuotaController : BaseApiController
{
    private readonly ITokenQuotaService _tokenQuotaService;

    public TokenQuotaController(ITokenQuotaService tokenQuotaService)
    {
        _tokenQuotaService = tokenQuotaService;
    }

    /// <summary>
    /// Tạo mới hoặc cập nhật cấu hình Token Quota cho 1 scope cụ thể.
    /// </summary>
    [HttpPost("config")]
    [Authorize(Roles = "Administrator")]
    public async Task<ActionResult<ApiResponse<TokenQuotaConfigDto>>> SetConfig(
        [FromBody] SetTokenQuotaConfigRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _tokenQuotaService.SetConfigAsync(GetCurrentUserId(), request, cancellationToken);
        return HandleResult(result, "Cấu hình Token Quota thành công.");
    }

    /// <summary>
    /// Danh sách toàn bộ cấu hình Token Quota hiện có.
    /// </summary>
    [HttpGet("config")]
    [Authorize(Roles = "Administrator")]
    public async Task<ActionResult<ApiResponse<List<TokenQuotaConfigDto>>>> ListConfigs(CancellationToken cancellationToken)
    {
        var result = await _tokenQuotaService.ListConfigsAsync(cancellationToken);
        return HandleResult(result, "Lấy danh sách Token Quota Config thành công.");
    }

    /// <summary>
    /// Xem mức dùng Token Quota hiện tại của 1 trẻ (Owner/Supervisor/Administrator).
    /// </summary>
    [HttpGet("child/{childProfileId:int}")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<TokenQuotaStatusDto>>> GetChildStatus(
        int childProfileId, CancellationToken cancellationToken)
    {
        var result = await _tokenQuotaService.GetStatusForChildAsync(GetCurrentUserId(), childProfileId, cancellationToken);
        return HandleResult(result, "Lấy thông tin Token Quota thành công.");
    }
}
