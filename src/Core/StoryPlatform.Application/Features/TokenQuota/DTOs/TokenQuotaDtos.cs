namespace StoryPlatform.Application.Features.TokenQuota.DTOs;

public class SetTokenQuotaConfigRequestDto
{
    public string Scope { get; set; } = string.Empty;
    public int? OrganizationId { get; set; }
    public int? ChildProfileId { get; set; }
    public int? UserId { get; set; }
    public int QuotaLimit { get; set; }
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
}

public class TokenQuotaConfigDto
{
    public int Id { get; set; }
    public string Scope { get; set; } = string.Empty;
    public int? OrganizationId { get; set; }
    public int? ChildProfileId { get; set; }
    public int? UserId { get; set; }
    public int QuotaLimit { get; set; }
    public int QuotaUsed { get; set; }
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
}

public class TokenQuotaStatusDto
{
    public bool IsUnlimited { get; set; }
    public string? Scope { get; set; }
    public int? QuotaLimit { get; set; }
    public int? QuotaUsed { get; set; }
    public int? Remaining { get; set; }
    public DateOnly? PeriodStart { get; set; }
    public DateOnly? PeriodEnd { get; set; }
}
