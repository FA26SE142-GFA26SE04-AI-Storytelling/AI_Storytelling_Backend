using System;

namespace StoryPlatform.Domain.Entities;

public class AuditLog : BaseEntity
{
    public int? ActorUserId { get; set; }
    public virtual UserAccount? ActorUser { get; set; }

    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public int EntityId { get; set; }
    public string? BeforeState { get; set; }
    public string? AfterState { get; set; }
    public DateTime OccurredAt { get; set; }
}
