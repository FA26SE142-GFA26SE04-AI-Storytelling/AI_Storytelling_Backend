using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.Auth.DTOs;
using StoryPlatform.Application.Features.Organizations.DTOs;
using StoryPlatform.Application.Features.Organizations.Interfaces;

namespace StoryPlatform.Api.Controllers;

public class OrganizationController : BaseApiController
{
    private readonly IOrganizationService _organizationService;

    public OrganizationController(IOrganizationService organizationService)
    {
        _organizationService = organizationService;
    }

    /// <summary>
    /// Administrator tạo tổ chức mới kèm tài khoản quản trị tổ chức (SchoolAdmin).
    /// Tổ chức được kích hoạt ngay vì Administrator trực tiếp tạo và xác thực.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Administrator")]
    public async Task<ActionResult<ApiResponse<CreateOrganizationResponseDto>>> CreateOrganization(
        [FromBody] CreateOrganizationRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _organizationService.CreateOrganizationAsync(
            GetCurrentUserId(), request, cancellationToken);
        return StatusCode(
            StatusCodes.Status201Created,
            ApiResponse<CreateOrganizationResponseDto>.Ok(
                result, "Tạo tổ chức và tài khoản quản trị tổ chức thành công."));
    }

    /// <summary>
    /// Quản trị tổ chức (SchoolAdmin) tạo tài khoản Giáo viên cho tổ chức của mình.
    /// </summary>
    [HttpPost("{id:int}/teachers")]
    [Authorize(Roles = "Teacher")]
    public async Task<ActionResult<ApiResponse<CreatedAccountDto>>> CreateTeacherAccount(
        int id, [FromBody] CreateTeacherAccountRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await _organizationService.CreateTeacherAccountAsync(
            id, GetCurrentUserId(), request, cancellationToken);
        return StatusCode(
            StatusCodes.Status201Created,
            ApiResponse<CreatedAccountDto>.Ok(result, "Tạo tài khoản giáo viên thành công."));
    }

    /// <summary>
    /// Danh sách tổ chức mà tài khoản hiện tại có membership Active.
    /// </summary>
    [HttpGet("mine")]
    [Authorize(Roles = "Parent,Teacher")]
    public async Task<ActionResult<ApiResponse<List<OrganizationDto>>>> ListMyOrganizations(
        CancellationToken cancellationToken)
    {
        var result = await _organizationService.ListMyOrganizationsAsync(
            GetCurrentUserId(), cancellationToken);
        return HandleResult(result, "Lấy danh sách tổ chức thành công.");
    }

    /// <summary>
    /// Chi tiết tổ chức; chỉ member Active được xem.
    /// </summary>
    [HttpGet("{id:int}")]
    [Authorize(Roles = "Parent,Teacher")]
    public async Task<ActionResult<ApiResponse<OrganizationDto>>> GetOrganizationById(
        int id, CancellationToken cancellationToken)
    {
        var result = await _organizationService.GetOrganizationByIdAsync(
            id, GetCurrentUserId(), cancellationToken);
        return HandleResult(result, "Lấy chi tiết tổ chức thành công.");
    }
}
