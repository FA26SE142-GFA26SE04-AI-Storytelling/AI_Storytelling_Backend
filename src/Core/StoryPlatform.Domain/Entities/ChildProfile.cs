using System;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

public class ChildProfile : BaseEntity
{
    public int OwnerUserId { get; set; }
    public virtual UserAccount? OwnerUser { get; set; }

    public string Nickname { get; set; } = string.Empty;
    public AgeBand AgeBand { get; set; }
    public string Language { get; set; } = "vi";
    public ChildProfileStatus Status { get; set; } = ChildProfileStatus.Draft;
    public ProfileScope Scope { get; set; } = ProfileScope.Personal;

    public int? OrganizationId { get; set; }
    public virtual Organization? Organization { get; set; }
}
