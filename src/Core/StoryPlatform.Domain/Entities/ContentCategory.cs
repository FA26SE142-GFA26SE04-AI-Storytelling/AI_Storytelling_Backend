namespace StoryPlatform.Domain.Entities;

public class ContentCategory : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    /// <summary>Các age_band bị chặn mặc định — preset Blocked ở Bước 1.4.</summary>
    public List<string> DefaultBlockedAgeBands { get; set; } = new();

    public int? CreatedByAdminId { get; set; }
    public virtual UserAccount? CreatedByAdmin { get; set; }
}
