using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.AuditLogs.DTOs;

namespace StoryPlatform.Application.Features.AuditLogs.Interfaces;

public interface IAuditLogQueryService
{
    Task<PagedResult<AuditLogDto>> GetMyAuditLogAsync(
        int currentUserId, PageRequest pageRequest, CancellationToken cancellationToken = default);

    Task<PagedResult<AuditLogDto>> GetChildProfileAuditLogAsync(
        int childProfileId, int currentUserId, PageRequest pageRequest,
        CancellationToken cancellationToken = default);

    /// <summary>Administrator xem audit log toàn hệ thống, mới nhất trước (Luồng 5).</summary>
    Task<PagedResult<AdminAuditLogDto>> SearchAsync(AdminAuditLogQuery query, CancellationToken cancellationToken = default);

    Task<AdminAuditLogDto> GetByIdAsync(int auditLogId, CancellationToken cancellationToken = default);

    /// <summary>Các action và loại đối tượng đã có trong audit log, sắp theo tên, để làm ô chọn bộ lọc.</summary>
    Task<AuditLogFiltersDto> GetFiltersAsync(CancellationToken cancellationToken = default);
}
