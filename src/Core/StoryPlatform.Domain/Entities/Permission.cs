namespace StoryPlatform.Domain.Entities;

/// <summary>Một quyền trong danh mục; danh mục được đồng bộ từ code lúc khởi động.</summary>
public class Permission : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string Group { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>Được gán cho role nhân viên qua API phân quyền.</summary>
    public bool IsStaffGrantable { get; set; }
}
