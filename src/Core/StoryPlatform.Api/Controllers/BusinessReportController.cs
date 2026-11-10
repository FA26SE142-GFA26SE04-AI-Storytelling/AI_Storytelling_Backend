using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.BusinessReports.DTOs;
using StoryPlatform.Application.Features.BusinessReports.Interfaces;

namespace StoryPlatform.Api.Controllers;

/// <summary>
/// Tổng hợp và publish Business Report định kỳ (Bước 5.5) và thống kê doanh thu theo kỳ (F-15).
/// </summary>
public class BusinessReportController : BaseApiController
{
    private readonly IBusinessReportService _businessReportService;
    private readonly IRevenueStatisticsService _revenueStatisticsService;

    public BusinessReportController(
        IBusinessReportService businessReportService, IRevenueStatisticsService revenueStatisticsService)
    {
        _businessReportService = businessReportService;
        _revenueStatisticsService = revenueStatisticsService;
    }

    /// <summary>
    /// Tổng hợp một Business Report mới cho một kỳ (trạng thái Compiling), kèm số liệu doanh thu chụp tại thời điểm tạo.
    /// Kỳ tính trọn ngày theo UTC, gồm cả ngày PeriodEnd, và không được chồng lên báo cáo đã có.
    /// </summary>
    [HttpPost("generate")]
    [Authorize(Roles = "Administrator")]
    public async Task<ActionResult<ApiResponse<AdminBusinessReportDto>>> Generate(
        [FromBody] GenerateBusinessReportRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _businessReportService.GenerateAsync(GetCurrentUserId(), request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created,
            ApiResponse<AdminBusinessReportDto>.Ok(result, "Tổng hợp Business Report thành công."));
    }

    /// <summary>
    /// Publish một Business Report đang Compiling để hiển thị trên Parent Dashboard.
    /// </summary>
    [HttpPost("{reportId:int}/publish")]
    [Authorize(Roles = "Administrator")]
    public async Task<ActionResult<ApiResponse<AdminBusinessReportDto>>> Publish(
        int reportId, CancellationToken cancellationToken)
    {
        var result = await _businessReportService.PublishAsync(GetCurrentUserId(), reportId, cancellationToken);
        return HandleResult(result, "Publish Business Report thành công.");
    }

    /// <summary>
    /// Lấy Business Report được publish gần nhất, phục vụ Parent Dashboard.
    /// </summary>
    [HttpGet("latest-published")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<BusinessReportDto>>> GetLatestPublished(
        CancellationToken cancellationToken)
    {
        var result = await _businessReportService.GetLatestPublishedAsync(cancellationToken);
        return HandleResult(result, "Lấy Business Report mới nhất thành công.");
    }

    /// <summary>
    /// Administrator xem doanh thu của một kỳ bất kỳ, tính trực tiếp từ giao dịch Paid (theo PaidAt) và sổ lượt.
    /// </summary>
    [HttpGet("admin/revenue")]
    [Authorize(Roles = "Administrator")]
    public async Task<ActionResult<ApiResponse<RevenueStatisticsDto>>> GetRevenue(
        [FromQuery] DateTime periodStart, [FromQuery] DateTime periodEnd, CancellationToken cancellationToken)
    {
        var result = await _revenueStatisticsService.GetAsync(periodStart, periodEnd, cancellationToken);
        return HandleResult(result, "Lấy thống kê doanh thu thành công.");
    }

    /// <summary>
    /// Administrator xem mọi Business Report, kỳ mới nhất trước, kèm số liệu doanh thu đã chụp.
    /// </summary>
    [HttpGet("admin")]
    [Authorize(Roles = "Administrator")]
    public async Task<ActionResult<ApiResponse<PagedResult<AdminBusinessReportDto>>>> List(
        [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var result = await _businessReportService.ListAsync(pageIndex, pageSize, cancellationToken);
        return HandleResult(result, "Lấy danh sách Business Report thành công.");
    }

    /// <summary>
    /// Administrator xem chi tiết một Business Report, kèm số liệu doanh thu đã chụp.
    /// </summary>
    [HttpGet("admin/{reportId:int}")]
    [Authorize(Roles = "Administrator")]
    public async Task<ActionResult<ApiResponse<AdminBusinessReportDto>>> GetById(
        int reportId, CancellationToken cancellationToken)
    {
        var result = await _businessReportService.GetByIdAsync(reportId, cancellationToken);
        return HandleResult(result, "Lấy Business Report thành công.");
    }
}
