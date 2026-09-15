using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Application.Common.Models;
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
    /// Tạo tổ chức mới. Người tạo trở thành SchoolAdmin; tổ chức chờ Administrator duyệt.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Teacher")]
    public async Task<ActionResult<ApiResponse<OrganizationDto>>> CreateOrganization(
        [FromBody] CreateOrganizationRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _organizationService.CreateOrganizationAsync(
            GetCurrentUserId(), request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created,
            ApiResponse<OrganizationDto>.Ok(result, "Tạo tổ chức thành công."));
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
