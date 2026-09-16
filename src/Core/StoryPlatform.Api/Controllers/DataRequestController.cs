using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.DataRequests.DTOs;
using StoryPlatform.Application.Features.DataRequests.Interfaces;

namespace StoryPlatform.Api.Controllers;

/// <summary>
/// Xử lý yêu cầu xuất/xóa dữ liệu cá nhân trẻ (Bước 5.4).
/// </summary>
public class DataRequestController : BaseApiController
{
    private readonly IDataRequestService _dataRequestService;

    public DataRequestController(IDataRequestService dataRequestService)
    {
        _dataRequestService = dataRequestService;
    }

    /// <summary>
    /// Tạo yêu cầu Export hoặc Delete dữ liệu cho Child Profile do chính mình sở hữu.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Parent")]
    public async Task<ActionResult<ApiResponse<DataRequestDto>>> CreateDataRequest(
        [FromBody] CreateDataRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _dataRequestService.CreateAsync(GetCurrentUserId(), request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created,
            ApiResponse<DataRequestDto>.Ok(result, "Tạo Data Request thành công."));
    }

    /// <summary>
    /// Lấy danh sách Data Request đang chờ Administrator xử lý.
    /// </summary>
    [HttpGet("pending")]
    [Authorize(Roles = "Administrator")]
    public async Task<ActionResult<ApiResponse<List<DataRequestDto>>>> ListPendingDataRequests(
        CancellationToken cancellationToken)
    {
        var result = await _dataRequestService.ListPendingAsync(cancellationToken);
        return HandleResult(result, "Lấy danh sách Data Request đang chờ xử lý thành công.");
    }

    /// <summary>
    /// Xử lý một Data Request loại Export: trả về file .xlsx tổng hợp dữ liệu của trẻ.
    /// </summary>
    [HttpPost("{dataRequestId:int}/resolve-export")]
    [Authorize(Roles = "Administrator")]
    public async Task<IActionResult> ResolveExport(
        int dataRequestId, [FromQuery] string format, CancellationToken cancellationToken)
    {
        var effectiveFormat = string.IsNullOrWhiteSpace(format) ? "xlsx" : format;
        var file = await _dataRequestService.ResolveExportAsync(
            GetCurrentUserId(), dataRequestId, effectiveFormat, cancellationToken);
        var contentType = string.Equals(effectiveFormat, "csv", StringComparison.OrdinalIgnoreCase)
            ? "application/zip"
            : "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
        return File(file.Content, contentType, file.FileName);
    }

    /// <summary>
    /// Xử lý một Data Request loại Delete: anonymize dữ liệu định danh của trẻ.
    /// </summary>
    [HttpPost("{dataRequestId:int}/resolve-delete")]
    [Authorize(Roles = "Administrator")]
    public async Task<ActionResult<ApiResponse<DataRequestDto>>> ResolveDelete(
        int dataRequestId, CancellationToken cancellationToken)
    {
        var result = await _dataRequestService.ResolveDeleteAsync(GetCurrentUserId(), dataRequestId, cancellationToken);
        return HandleResult(result, "Xử lý Data Request Delete thành công.");
    }
}
