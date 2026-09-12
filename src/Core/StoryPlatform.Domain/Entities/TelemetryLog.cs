using System;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

public class TelemetryLog : BaseEntity
{
    public int ReadingSessionId { get; set; }
    public virtual ReadingSession? ReadingSession { get; set; }

    public TelemetryEventType EventType { get; set; }
    public string? EventPayload { get; set; }
    public DateTime OccurredAt { get; set; }
}
