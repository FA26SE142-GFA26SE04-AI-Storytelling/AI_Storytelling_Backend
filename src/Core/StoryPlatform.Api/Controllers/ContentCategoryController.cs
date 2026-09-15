using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.ContentCategories.DTOs;
using StoryPlatform.Application.Features.ContentCategories.Interfaces;

namespace StoryPlatform.Api.Controllers;

/// <summary>
/// Quản lý danh mục nội dung dùng chung.
/// </summary>
public class ContentCategoryController : BaseApiController
{
    private readonly IContentCategoryService _contentCategoryService;

    public ContentCategoryController(IContentCategoryService contentCategoryService)
    {
        _contentCategoryService = contentCategoryService;
    }

    /// <summary>
    /// Tạo mới một danh mục nội dung.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Administrator")]
    public async Task<ActionResult<ApiResponse<ContentCategoryDto>>> CreateContentCategory(
        [FromBody] CreateContentCategoryRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _contentCategoryService.CreateAsync(GetCurrentUserId(), request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created,
            ApiResponse<ContentCategoryDto>.Ok(result, "Tạo Content Category thành công."));
    }

    /// <summary>
    /// Cập nhật tên hiển thị và trạng thái hoạt động của danh mục nội dung.
    /// </summary>
    [HttpPut("{contentCategoryId:int}")]
    [Authorize(Roles = "Administrator")]
    public async Task<ActionResult<ApiResponse<ContentCategoryDto>>> UpdateContentCategory(
        int contentCategoryId, [FromBody] UpdateContentCategoryRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await _contentCategoryService.UpdateAsync(contentCategoryId, request, cancellationToken);
        return HandleResult(result, "Cập nhật Content Category thành công.");
    }

    /// <summary>
    /// Xoá hẳn một danh mục nội dung.
    /// </summary>
    [HttpDelete("{contentCategoryId:int}")]
    [Authorize(Roles = "Administrator")]
    public async Task<ActionResult<ApiResponse<object?>>> DeleteContentCategory(
        int contentCategoryId, CancellationToken cancellationToken)
    {
        await _contentCategoryService.DeleteAsync(contentCategoryId, cancellationToken);
        return HandleResult<object?>(null, "Xoá Content Category thành công.");
    }

    /// <summary>
    /// Lấy chi tiết một danh mục nội dung theo mã định danh.
    /// </summary>
    [HttpGet("{contentCategoryId:int}")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<ContentCategoryDto>>> GetContentCategory(
        int contentCategoryId, CancellationToken cancellationToken)
    {
        var result = await _contentCategoryService.GetByIdAsync(contentCategoryId, cancellationToken);
        return HandleResult(result, "Lấy Content Category thành công.");
    }

    /// <summary>
    /// Lấy danh sách toàn bộ danh mục nội dung.
    /// </summary>
    [HttpGet]
    [Authorize]
    public async Task<ActionResult<ApiResponse<List<ContentCategoryDto>>>> ListContentCategories(
        CancellationToken cancellationToken)
    {
        var result = await _contentCategoryService.ListAsync(cancellationToken);
        return HandleResult(result, "Lấy danh sách Content Category thành công.");
    }
}
