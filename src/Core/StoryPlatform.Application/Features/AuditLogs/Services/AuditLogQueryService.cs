using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.AuditLogs.DTOs;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.Supervision.Interfaces;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Application.Features.AuditLogs.Services;

public class AuditLogQueryService : IAuditLogQueryService
{
    private static readonly string[] ChildProfileRelatedEntityTypes = { nameof(ChildProfile) };

    private readonly IUnitOfWork _unitOfWork;
    private readonly ISupervisionAccessGuard _accessGuard;

    public AuditLogQueryService(IUnitOfWork unitOfWork, ISupervisionAccessGuard accessGuard)
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
        await _accessGuard.EnsureActiveSupervisionAsync(
            childProfileId, currentUserId, cancellationToken);

        var (items, totalCount) = await _unitOfWork.Repository<AuditLog>().GetPagedAsync(
            pageRequest.PageIndex,
            pageRequest.PageSize,
            filter: log => log.EntityId == childProfileId
                           && ChildProfileRelatedEntityTypes.Contains(log.EntityType),
            orderBy: query => query.OrderByDescending(log => log.OccurredAt),
            cancellationToken: cancellationToken);

        return new PagedResult<AuditLogDto>(
            items.Select(MapAuditLog).ToList(), totalCount, pageRequest.PageIndex, pageRequest.PageSize);
    }

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
