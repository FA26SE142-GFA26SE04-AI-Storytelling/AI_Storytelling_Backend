using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.ChildProfiles.ClassGroups.DTOs;
using StoryPlatform.Application.Features.ChildProfiles.ClassGroups.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.DTOs;

namespace StoryPlatform.Api.Controllers;

public class ClassGroupController : BaseApiController
{
    private readonly IClassGroupService _classGroupService;

    public ClassGroupController(IClassGroupService classGroupService)
    {
        _classGroupService = classGroupService;
    }

    /// <summary>
    /// Tạo Class Group mới (Bước 1.9b) — actor là Teacher, không phải Parent.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Teacher")]
    public async Task<ActionResult<ApiResponse<ClassGroupDto>>> CreateClassGroup(
        [FromBody] CreateClassGroupRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await _classGroupService.CreateClassGroupAsync(
            GetCurrentUserId(), request, cancellationToken);

        return StatusCode(StatusCodes.Status201Created,
            ApiResponse<ClassGroupDto>.Ok(result, "Tạo Class Group thành công."));
    }

    /// <summary>
    /// Danh sách Class Group do chính giáo viên đang đăng nhập phụ trách.
    /// </summary>
    [HttpGet("mine")]
    [Authorize(Roles = "Teacher")]
    public async Task<ActionResult<ApiResponse<List<ClassGroupDto>>>> ListMyClassGroups(
        CancellationToken cancellationToken)
    {
        var result = await _classGroupService.ListMyClassGroupsAsync(GetCurrentUserId(), cancellationToken);
        return HandleResult(result, "Lấy danh sách Class Group thành công.");
    }

    /// <summary>
    /// Lấy chi tiết 1 Class Group.
    /// </summary>
    [HttpGet("{id:int}")]
    [Authorize(Roles = "Teacher")]
    public async Task<ActionResult<ApiResponse<ClassGroupDto>>> GetClassGroupById(
        int id, CancellationToken cancellationToken)
    {
        var result = await _classGroupService.GetClassGroupByIdAsync(id, GetCurrentUserId(), cancellationToken);
        return HandleResult(result, "Lấy chi tiết Class Group thành công.");
    }

    /// <summary>
    /// Đổi tên Class Group.
    /// </summary>
    [HttpPut("{id:int}")]
    [Authorize(Roles = "Teacher")]
    public async Task<ActionResult<ApiResponse<ClassGroupDto>>> UpdateClassGroup(
        int id, [FromBody] UpdateClassGroupRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _classGroupService.UpdateClassGroupAsync(id, GetCurrentUserId(), request, cancellationToken);
        return HandleResult(result, "Cập nhật Class Group thành công.");
    }

    /// <summary>
    /// Lưu trữ (archive) Class Group — dừng dùng cho giao bài mới, giữ nguyên lịch sử.
    /// </summary>
    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Teacher")]
    public async Task<ActionResult<ApiResponse<object?>>> ArchiveClassGroup(
        int id, CancellationToken cancellationToken)
    {
        await _classGroupService.ArchiveClassGroupAsync(id, GetCurrentUserId(), cancellationToken);
        return HandleResult<object?>(null, "Lưu trữ Class Group thành công.");
    }

    /// <summary>
    /// Thêm 1 hồ sơ trẻ vào Class Group (Bước 1.9b) — tự động backfill shared_story_recipients.
    /// </summary>
    [HttpPost("{classGroupId:int}/members/{childProfileId:int}")]
    [Authorize(Roles = "Teacher")]
    public async Task<ActionResult<ApiResponse<object?>>> AddChildToClassGroup(
        int classGroupId,
        int childProfileId,
        CancellationToken cancellationToken)
    {
        await _classGroupService.AddChildToClassGroupAsync(
            classGroupId, childProfileId, GetCurrentUserId(), cancellationToken);

        return HandleResult<object?>(null, "Thêm hồ sơ trẻ vào Class Group thành công.");
    }

    /// <summary>
    /// Bulk Enrollment (Bước 1.5) — import CSV, mỗi dòng hợp lệ tạo một ChildProfile và invitation code riêng.
    /// </summary>
    [HttpPost("{classGroupId:int}/bulk-enroll")]
    [Authorize(Roles = "Teacher")]
    [RequestSizeLimit(
        StoryPlatform.Application.Features.ChildProfiles.ClassGroups.BulkEnrollment.CsvBulkEnrollmentParser.MaxFileSizeBytes)]
    public async Task<ActionResult<ApiResponse<BulkEnrollResultDto>>> BulkEnroll(
        int classGroupId,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(ApiResponse<object?>.Fail("File CSV không được để trống."));
        }

        await using var stream = new MemoryStream();
        await file.CopyToAsync(stream, cancellationToken);

        var result = await _classGroupService.BulkEnrollAsync(
            classGroupId, GetCurrentUserId(), stream.ToArray(), cancellationToken);

        return HandleResult(result, "Xử lý import hàng loạt hoàn tất.");
    }

    /// <summary>
    /// Danh sách hồ sơ trẻ đang là thành viên của Class Group.
    /// </summary>
    [HttpGet("{classGroupId:int}/members")]
    [Authorize(Roles = "Teacher")]
    public async Task<ActionResult<ApiResponse<List<ChildProfileDto>>>> ListClassGroupMembers(
        int classGroupId, CancellationToken cancellationToken)
    {
        var result = await _classGroupService.ListClassGroupMembersAsync(classGroupId, GetCurrentUserId(), cancellationToken);
        return HandleResult(result, "Lấy danh sách thành viên Class Group thành công.");
    }

    /// <summary>
    /// Gỡ 1 hồ sơ trẻ khỏi Class Group (xoá cứng, không giữ lịch sử membership).
    /// </summary>
    [HttpDelete("{classGroupId:int}/members/{childProfileId:int}")]
    [Authorize(Roles = "Teacher")]
    public async Task<ActionResult<ApiResponse<object?>>> RemoveChildFromClassGroup(
        int classGroupId, int childProfileId, CancellationToken cancellationToken)
    {
        await _classGroupService.RemoveChildFromClassGroupAsync(classGroupId, childProfileId, GetCurrentUserId(), cancellationToken);
        return HandleResult<object?>(null, "Gỡ hồ sơ trẻ khỏi Class Group thành công.");
    }
}
