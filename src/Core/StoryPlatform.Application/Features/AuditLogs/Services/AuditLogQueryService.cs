using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.AuditLogs.DTOs;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.Interfaces;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Application.Features.AuditLogs.Services;

public class AuditLogQueryService : IAuditLogQueryService
{
    private const string ChildProfileEntityType = nameof(ChildProfile);

    private readonly IUnitOfWork _unitOfWork;
    private readonly IChildProfileAccessGuard _accessGuard;

    public AuditLogQueryService(IUnitOfWork unitOfWork, IChildProfileAccessGuard accessGuard)
    {
        _unitOfWork = unitOfWork;
        _accessGuard = accessGuard;
    }

    public async Task<PagedResult<AuditLogDto>> GetMyAuditLogAsync(
        int currentUserId, PageRequest pageRequest, CancellationToken cancellationToken = default)
    {
        var (items, totalCount) = await _unitOfWork.Repository<AuditLog>().GetPagedAsync(
            pageRequest.PageIndex,
            pageRequest.PageSize,
            filter: log => log.ActorUserId == currentUserId,
            orderBy: query => query.OrderByDescending(log => log.OccurredAt),
            cancellationToken: cancellationToken);

        return new PagedResult<AuditLogDto>(
            items.Select(MapAuditLog).ToList(), totalCount, pageRequest.PageIndex, pageRequest.PageSize);
    }

    public async Task<PagedResult<AuditLogDto>> GetChildProfileAuditLogAsync(
        int childProfileId, int currentUserId, PageRequest pageRequest,
        CancellationToken cancellationToken = default)
    {
        await _accessGuard.EnsureOwnerAsync(
            childProfileId, currentUserId, cancellationToken);

        var (items, totalCount) = await _unitOfWork.Repository<AuditLog>().GetPagedAsync(
            pageRequest.PageIndex,
            pageRequest.PageSize,
            // .NET 10 dịch string[].Contains thành MemoryExtensions.Contains(ReadOnlySpan), EF Core không dịch được (lỗi 500).
            filter: log => log.EntityId == childProfileId
                           && log.EntityType == ChildProfileEntityType,
            orderBy: query => query.OrderByDescending(log => log.OccurredAt),
            cancellationToken: cancellationToken);

        return new PagedResult<AuditLogDto>(
            items.Select(MapAuditLog).ToList(), totalCount, pageRequest.PageIndex, pageRequest.PageSize);
    }

    public async Task<PagedResult<AdminAuditLogDto>> SearchAsync(
        AdminAuditLogQuery query, CancellationToken cancellationToken = default)
    {
        if (query.From is { } from && query.To is { } to && to < from)
        {
            throw new BadRequestException("Thời điểm kết thúc không được trước thời điểm bắt đầu.");
        }

        if (query.SystemOnly && query.ActorUserId != null)
        {
            throw new BadRequestException("Không lọc cùng lúc theo người làm và log của hệ thống.");
        }

        var page = new PageRequest { PageIndex = Math.Max(1, query.PageIndex), PageSize = query.PageSize };
        var actorUserId = query.ActorUserId;
        var systemOnly = query.SystemOnly;
        var action = string.IsNullOrWhiteSpace(query.Action) ? null : query.Action.Trim();
        var entityType = string.IsNullOrWhiteSpace(query.EntityType) ? null : query.EntityType.Trim();
        var entityId = query.EntityId;
        var fromUtc = query.From.HasValue ? ToUtc(query.From.Value) : (DateTime?)null;
        var toUtc = query.To.HasValue ? ToUtc(query.To.Value) : (DateTime?)null;

        var (items, totalCount) = await _unitOfWork.Repository<AuditLog>().GetPagedAsync(
            page.PageIndex,
            page.PageSize,
            filter: log => (actorUserId == null || log.ActorUserId == actorUserId)
                           && (!systemOnly || log.ActorUserId == null)
                           && (action == null || log.Action == action)
                           && (entityType == null || log.EntityType == entityType)
                           && (entityId == null || log.EntityId == entityId)
                           && (fromUtc == null || log.OccurredAt >= fromUtc)
                           && (toUtc == null || log.OccurredAt <= toUtc),
            orderBy: logs => logs.OrderByDescending(log => log.OccurredAt).ThenByDescending(log => log.Id),
            includeProperties: nameof(AuditLog.ActorUser),
            cancellationToken: cancellationToken);

        return new PagedResult<AdminAuditLogDto>(
            items.Select(MapAdminAuditLog).ToList(), totalCount, page.PageIndex, page.PageSize);
    }

    public async Task<AdminAuditLogDto> GetByIdAsync(int auditLogId, CancellationToken cancellationToken = default)
    {
        var log = await _unitOfWork.Repository<AuditLog>().FirstOrDefaultAsync(
                      item => item.Id == auditLogId, nameof(AuditLog.ActorUser), cancellationToken)
                  ?? throw new NotFoundException("Audit log", auditLogId);
        return MapAdminAuditLog(log);
    }

    public Task<AuditLogFiltersDto> GetFiltersAsync(CancellationToken cancellationToken = default)
    {
        var logs = _unitOfWork.Repository<AuditLog>().Query();
        return Task.FromResult(new AuditLogFiltersDto
        {
            Actions = logs.Select(log => log.Action).Distinct().OrderBy(name => name).ToList(),
            EntityTypes = logs.Select(log => log.EntityType).Distinct().OrderBy(name => name).ToList()
        });
    }

    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private static AdminAuditLogDto MapAdminAuditLog(AuditLog log) => new()
    {
        Id = log.Id,
        ActorUserId = log.ActorUserId,
        ActorUsername = log.ActorUser?.Username,
        ActorFullName = log.ActorUser?.FullName,
        ActorRole = log.ActorUser?.Role.ToString(),
        Action = log.Action,
        EntityType = log.EntityType,
        EntityId = log.EntityId,
        OccurredAt = log.OccurredAt,
        BeforeState = log.BeforeState,
        AfterState = log.AfterState
    };

    private static AuditLogDto MapAuditLog(AuditLog log) => new()
    {
        Id = log.Id,
        ActorUserId = log.ActorUserId,
        Action = log.Action,
        EntityType = log.EntityType,
        EntityId = log.EntityId,
        OccurredAt = log.OccurredAt
    };
}
