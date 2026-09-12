using System;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

public class PromptCatalogVersion : BaseEntity
{
    public string VersionNo { get; set; } = string.Empty;
    public string GradeBand { get; set; } = string.Empty;
    public string? RestrictedKeywords { get; set; }
    public CatalogStatus Status { get; set; } = CatalogStatus.Draft;
    public DateTime? EffectiveDate { get; set; }

    public int CreatedByAdminId { get; set; }
    public virtual UserAccount? CreatedByAdmin { get; set; }
}
