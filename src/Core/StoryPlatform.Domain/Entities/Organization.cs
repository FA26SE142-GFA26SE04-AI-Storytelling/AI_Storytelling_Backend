using System;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

public class Organization : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? ContactEmail { get; set; }
    public OrgVerification VerificationStatus { get; set; } = OrgVerification.PendingVerification;

    public int CreatedByUserId { get; set; }
    public virtual UserAccount? CreatedByUser { get; set; }

    public int? VerifiedByAdminId { get; set; }
    public virtual UserAccount? VerifiedByAdmin { get; set; }

    public int? SuspendedByAdminId { get; set; }
    public virtual UserAccount? SuspendedByAdmin { get; set; }

    public DateTime? SuspendedAt { get; set; }
    public string? SuspensionReason { get; set; }

    public string? RejectionReason { get; set; }
    public DateTime? ReactivatedAt { get; set; }
    public int? ReactivatedByAdminId { get; set; }
    public virtual UserAccount? ReactivatedByAdmin { get; set; }
    public DateTime? ClosureRequestedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
}
