using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.BusinessReports.DTOs;
using StoryPlatform.Application.Features.BusinessReports.Interfaces;

namespace StoryPlatform.Api.Controllers;

/// <summary>
/// Tổng hợp và publish Business Report định kỳ (Bước 5.5).
/// </summary>
public class BusinessReportController : BaseApiController
{
    private readonly IBusinessReportService _businessReportService;

    public BusinessReportController(IBusinessReportService businessReportService)
    {
        _businessReportService = businessReportService;
    }

    /// <summary>
    /// Tổng hợp một Business Report mới cho một kỳ báo cáo (trạng thái Compiling).
    /// </summary>
    [HttpPost("generate")]
    [Authorize(Roles = "Administrator")]
    public async Task<ActionResult<ApiResponse<BusinessReportDto>>> Generate(
        [FromBody] GenerateBusinessReportRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _businessReportService.GenerateAsync(GetCurrentUserId(), request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created,
            ApiResponse<BusinessReportDto>.Ok(result, "Tổng hợp Business Report thành công."));
    }

    /// <summary>
    /// Publish một Business Report đang Compiling để hiển thị trên Parent/Teacher Dashboard.
    /// </summary>
    [HttpPost("{reportId:int}/publish")]
    [Authorize(Roles = "Administrator")]
    public async Task<ActionResult<ApiResponse<BusinessReportDto>>> Publish(
        int reportId, CancellationToken cancellationToken)
    {
        var result = await _businessReportService.PublishAsync(GetCurrentUserId(), reportId, cancellationToken);
        return HandleResult(result, "Publish Business Report thành công.");
    }

    /// <summary>
    /// Lấy Business Report được publish gần nhất, phục vụ Parent/Teacher Dashboard.
    /// </summary>
    [HttpGet("latest-published")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<BusinessReportDto>>> GetLatestPublished(
        CancellationToken cancellationToken)
    {
        var result = await _businessReportService.GetLatestPublishedAsync(cancellationToken);
        return HandleResult(result, "Lấy Business Report mới nhất thành công.");
    }
}
