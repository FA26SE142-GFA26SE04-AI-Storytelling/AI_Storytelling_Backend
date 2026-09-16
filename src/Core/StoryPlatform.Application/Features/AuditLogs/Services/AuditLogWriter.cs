using System.Text.Json;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Application.Features.AuditLogs.Services;

public class AuditLogWriter : IAuditLogWriter
{
    private readonly IUnitOfWork _unitOfWork;

    public AuditLogWriter(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task LogAsync(
        int? actorUserId,
        string action,
        string entityType,
        int entityId,
        object? beforeState,
        object? afterState,
        CancellationToken cancellationToken = default)
    {
        var log = new AuditLog
        {
            ActorUserId = actorUserId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            BeforeState = beforeState == null ? null : JsonSerializer.Serialize(beforeState),
            AfterState = afterState == null ? null : JsonSerializer.Serialize(afterState),
            OccurredAt = DateTime.UtcNow
        };

        await _unitOfWork.Repository<AuditLog>().AddAsync(log, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
