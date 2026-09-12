using System;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

// TODO: CHECK constraint — DBML note: partial unique index (child_profile_id, organization_id) WHERE revoked_at IS NULL; requires raw SQL migration.
public class OrgConsentRecord : BaseEntity
{
    public int ChildProfileId { get; set; }
    public virtual ChildProfile? ChildProfile { get; set; }

    public int OrganizationId { get; set; }
    public virtual Organization? Organization { get; set; }

    public ConsentStatus Status { get; set; } = ConsentStatus.Pending;

    public int? DecidedByUserId { get; set; }
    public virtual UserAccount? DecidedByUser { get; set; }

    public DateTime? DecidedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
}
