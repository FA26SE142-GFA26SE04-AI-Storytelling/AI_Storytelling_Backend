using System;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

public class TokenQuotaConfig : BaseEntity
{
    public TokenQuotaScope Scope { get; set; }

    public int? OrganizationId { get; set; }
    public virtual Organization? Organization { get; set; }

    public int? ChildProfileId { get; set; }
    public virtual ChildProfile? ChildProfile { get; set; }

    public int QuotaLimit { get; set; }
    public int QuotaUsed { get; set; } = 0;
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
}
