namespace StoryPlatform.Application.Features.AuditLogs.DTOs;

public class AuditLogDto
{
    public int Id { get; set; }
    public int? ActorUserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public int EntityId { get; set; }
    public DateTime OccurredAt { get; set; }
}
