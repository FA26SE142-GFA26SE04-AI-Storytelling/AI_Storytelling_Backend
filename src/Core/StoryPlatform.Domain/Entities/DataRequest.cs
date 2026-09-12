using System;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

public class DataRequest : BaseEntity
{
    public int RequestedByUserId { get; set; }
    public virtual UserAccount? RequestedByUser { get; set; }

    public int? ChildProfileId { get; set; }
    public virtual ChildProfile? ChildProfile { get; set; }

    public DataRequestType RequestType { get; set; }
    public DataRequestStatus Status { get; set; } = DataRequestStatus.Pending;
    public DateTime? ResolvedAt { get; set; }
}
