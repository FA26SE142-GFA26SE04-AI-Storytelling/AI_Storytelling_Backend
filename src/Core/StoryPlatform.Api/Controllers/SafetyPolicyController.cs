using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.ChildProfiles.Safety.DTOs;
using StoryPlatform.Application.Features.ChildProfiles.Safety.Interfaces;

namespace StoryPlatform.Api.Controllers;

public class SafetyPolicyController : BaseApiController
{
    private readonly ISafetyPolicyService _safetyPolicyService;

    public SafetyPolicyController(ISafetyPolicyService safetyPolicyService)
    {
        _safetyPolicyService = safetyPolicyService;
    }

    /// <summary>
    /// Tạo/cập nhật Safety Policy của một hồ sơ trẻ (Bước 1.4) — yêu cầu quyền ManageSafetySettings.
    /// </summary>
    [HttpPut("{childProfileId:int}")]
    [Authorize(Roles = "Parent,Teacher")]
    public async Task<ActionResult<ApiResponse<SafetyPolicyDto>>> SetSafetyPolicy(
        int childProfileId,
        [FromBody] SetSafetyPolicyRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await _safetyPolicyService.SetSafetyPolicyAsync(
            childProfileId, GetCurrentUserId(), request, cancellationToken);

        return HandleResult(result, "Lưu Safety Policy thành công.");
    }

    /// <summary>
    /// Tạo mới Safety Policy của một hồ sơ trẻ (dùng cùng logic upsert với PUT) — yêu cầu quyền ManageSafetySettings.
    /// </summary>
    [HttpPost("{childProfileId:int}")]
    [Authorize(Roles = "Parent,Teacher")]
    public async Task<ActionResult<ApiResponse<SafetyPolicyDto>>> CreateSafetyPolicy(
        int childProfileId,
        [FromBody] SetSafetyPolicyRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await _safetyPolicyService.SetSafetyPolicyAsync(
            childProfileId, GetCurrentUserId(), request, cancellationToken);

        return HandleResult(result, "Tạo Safety Policy thành công.");
    }

    /// <summary>
    /// Lấy Safety Policy hiện tại của hồ sơ trẻ.
    /// </summary>
    [HttpGet("{childProfileId:int}")]
    [Authorize(Roles = "Parent,Teacher")]
    public async Task<ActionResult<ApiResponse<SafetyPolicyDto>>> GetSafetyPolicy(
        int childProfileId, CancellationToken cancellationToken)
    {
        var result = await _safetyPolicyService.GetSafetyPolicyAsync(childProfileId, GetCurrentUserId(), cancellationToken);
        return HandleResult(result, "Lấy Safety Policy thành công.");
    }

    /// <summary>
    /// Xoá hẳn Safety Policy của hồ sơ trẻ (xoá cứng — chỉ là cấu hình).
    /// </summary>
    [HttpDelete("{childProfileId:int}")]
    [Authorize(Roles = "Parent,Teacher")]
    public async Task<ActionResult<ApiResponse<object?>>> DeleteSafetyPolicy(
        int childProfileId, CancellationToken cancellationToken)
    {
        await _safetyPolicyService.DeleteSafetyPolicyAsync(childProfileId, GetCurrentUserId(), cancellationToken);
        return HandleResult<object?>(null, "Xoá Safety Policy thành công.");
    }
}
