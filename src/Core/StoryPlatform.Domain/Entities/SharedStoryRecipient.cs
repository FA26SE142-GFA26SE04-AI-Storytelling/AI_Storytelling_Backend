using System;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

public class SharedStoryRecipient : BaseEntity
{
    public int SharedStoryId { get; set; }
    public virtual SharedStory? SharedStory { get; set; }

    public int RecipientUserId { get; set; }
    public virtual UserAccount? RecipientUser { get; set; }

    public RecipientStatus Status { get; set; } = RecipientStatus.Pending;
    public DateTime? RespondedAt { get; set; }
}
