namespace StoryPlatform.Domain.Entities;

public class ContentCategory : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    public int? CreatedByAdminId { get; set; }
    public virtual UserAccount? CreatedByAdmin { get; set; }
}
