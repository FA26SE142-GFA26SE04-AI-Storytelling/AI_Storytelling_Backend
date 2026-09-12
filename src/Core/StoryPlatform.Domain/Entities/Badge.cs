using System;

namespace StoryPlatform.Domain.Entities;

public class Badge : BaseEntity
{
    public int ChildProfileId { get; set; }
    public virtual ChildProfile? ChildProfile { get; set; }

    public string BadgeCode { get; set; } = string.Empty;
    public DateTime EarnedAt { get; set; }
}
