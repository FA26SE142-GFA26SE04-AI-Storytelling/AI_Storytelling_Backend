using System;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

public class Notification : BaseEntity
{
    public int RecipientUserId { get; set; }
    public virtual UserAccount? RecipientUser { get; set; }

    public NotificationType Type { get; set; }
    public string? Payload { get; set; }
    public NotificationReadStatus Status { get; set; } = NotificationReadStatus.Unread;
    public DateTime? ReadAt { get; set; }
}
