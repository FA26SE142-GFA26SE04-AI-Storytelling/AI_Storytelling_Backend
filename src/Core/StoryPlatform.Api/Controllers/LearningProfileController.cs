using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.ChildProfiles.Learning.DTOs;
using StoryPlatform.Application.Features.ChildProfiles.Learning.Interfaces;
using StoryPlatform.Api.Authorization;
using StoryPlatform.Application.Features.AccessControl;

namespace StoryPlatform.Api.Controllers;

public class LearningProfileController : BaseApiController
{
    private readonly ILearningProfileService _learningProfileService;

    public LearningProfileController(ILearningProfileService learningProfileService)
    {
        _learningProfileService = learningProfileService;
    }

    /// <summary>
    /// Tạo/cập nhật Learning Profile của một hồ sơ trẻ (Bước 1.3).
    /// </summary>
    [HttpPut("{childProfileId:int}")]
    [HasPermission(Permissions.Parent.ChildProfileManage)]
    public async Task<ActionResult<ApiResponse<LearningProfileDto>>> SetLearningProfile(
        int childProfileId,
        [FromBody] SetLearningProfileRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await _learningProfileService.SetLearningProfileAsync(
            childProfileId, GetCurrentUserId(), request, cancellationToken);

        return HandleResult(result, "Lưu Learning Profile thành công.");
    }

    /// <summary>
    /// Tạo mới Learning Profile của một hồ sơ trẻ (dùng cùng logic upsert với PUT).
    /// </summary>
    [HttpPost("{childProfileId:int}")]
    [HasPermission(Permissions.Parent.ChildProfileManage)]
    public async Task<ActionResult<ApiResponse<LearningProfileDto>>> CreateLearningProfile(
        int childProfileId,
        [FromBody] SetLearningProfileRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await _learningProfileService.SetLearningProfileAsync(
            childProfileId, GetCurrentUserId(), request, cancellationToken);

        return HandleResult(result, "Tạo Learning Profile thành công.");
    }

    /// <summary>
    /// Lấy Learning Profile hiện tại của hồ sơ trẻ.
    /// </summary>
    [HttpGet("{childProfileId:int}")]
    [HasPermission(Permissions.Parent.ChildProfileManage)]
    public async Task<ActionResult<ApiResponse<LearningProfileDto>>> GetLearningProfile(
        int childProfileId, CancellationToken cancellationToken)
    {
        var result = await _learningProfileService.GetLearningProfileAsync(childProfileId, GetCurrentUserId(), cancellationToken);
        return HandleResult(result, "Lấy Learning Profile thành công.");
    }

    /// <summary>
    /// Xoá hẳn Learning Profile của hồ sơ trẻ (xoá cứng — chỉ là cấu hình, không phải lịch sử học tập).
    /// </summary>
    [HttpDelete("{childProfileId:int}")]
    [HasPermission(Permissions.Parent.ChildProfileManage)]
    public async Task<ActionResult<ApiResponse<object?>>> DeleteLearningProfile(
        int childProfileId, CancellationToken cancellationToken)
    {
        await _learningProfileService.DeleteLearningProfileAsync(childProfileId, GetCurrentUserId(), cancellationToken);
        return HandleResult<object?>(null, "Xoá Learning Profile thành công.");
    }
}
