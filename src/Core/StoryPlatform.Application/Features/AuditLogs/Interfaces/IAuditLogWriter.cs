namespace StoryPlatform.Application.Features.AuditLogs.Interfaces;

/// <summary>
/// Ghi nhật ký kiểm toán cho các thao tác nhạy cảm trong hệ thống.
/// </summary>
public interface IAuditLogWriter
{
    Task LogAsync(
        int? actorUserId,
        string action,
        string entityType,
        int entityId,
        object? beforeState,
        object? afterState,
        CancellationToken cancellationToken = default);
}
