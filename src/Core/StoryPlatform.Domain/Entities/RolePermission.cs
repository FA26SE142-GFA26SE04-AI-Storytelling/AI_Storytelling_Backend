namespace StoryPlatform.Domain.Entities;

/// <summary>Role được cấp permission; khóa chính kép (RoleId, PermissionId).</summary>
public class RolePermission
{
    public int RoleId { get; set; }
    public virtual Role Role { get; set; } = null!;

    public int PermissionId { get; set; }
    public virtual Permission Permission { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
