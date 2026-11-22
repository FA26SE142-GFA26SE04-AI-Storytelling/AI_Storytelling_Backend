using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.AuditLogs.DTOs;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Api.Authorization;
using StoryPlatform.Application.Features.AccessControl;

namespace StoryPlatform.Api.Controllers;

/// <summary>
/// Administrator xem audit log toàn hệ thống (Luồng 5): lọc theo người làm, action, đối tượng và khoảng thời gian.
/// Chỉ đọc; audit log không sửa, không xoá qua API.
/// </summary>
[HasPermission(Permissions.Admin.AuditView)]
[Route("api/v1/admin/audit-logs")]
public class AdminAuditLogController : BaseApiController
{
    private readonly IAuditLogQueryService _service;

    public AdminAuditLogController(IAuditLogQueryService service)
    {
        _service = service;
    }

    /// <summary>
    /// Danh sách audit log, mới nhất trước. Lọc tuỳ chọn: actorUserId, systemOnly (log do hệ thống tự ghi), action,
    /// entityType, entityId, from, to (UTC, gồm cả hai mốc). pageSize tối đa 100.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<AdminAuditLogDto>>>> Search(
        [FromQuery] AdminAuditLogQuery query, CancellationToken cancellationToken)
    {
        var result = await _service.SearchAsync(query, cancellationToken);
        return HandleResult(result, "Lấy audit log thành công.");
    }

    /// <summary>Các action và loại đối tượng đã có trong audit log, để làm ô chọn bộ lọc.</summary>
    [HttpGet("filters")]
    public async Task<ActionResult<ApiResponse<AuditLogFiltersDto>>> GetFilters(CancellationToken cancellationToken)
    {
        var result = await _service.GetFiltersAsync(cancellationToken);
        return HandleResult(result, "Lấy bộ lọc audit log thành công.");
    }

    /// <summary>Một dòng audit log kèm trạng thái trước/sau.</summary>
    [HttpGet("{auditLogId:int}")]
    public async Task<ActionResult<ApiResponse<AdminAuditLogDto>>> Get(int auditLogId, CancellationToken cancellationToken)
    {
        var result = await _service.GetByIdAsync(auditLogId, cancellationToken);
        return HandleResult(result, "Lấy audit log thành công.");
    }
}
