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
}
