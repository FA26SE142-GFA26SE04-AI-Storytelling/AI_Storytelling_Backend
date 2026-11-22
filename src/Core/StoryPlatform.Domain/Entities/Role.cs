namespace StoryPlatform.Domain.Entities;

/// <summary>Role trong bảng phân quyền; <c>Code</c> trùng giá trị claim role của JWT.</summary>
public class Role : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>Role nhân viên: Administrator sửa được quyền của role này.</summary>
    public bool IsStaff { get; set; }

    /// <summary>Role hệ thống, không xóa được.</summary>
    public bool IsSystem { get; set; }

    public virtual ICollection<RolePermission> Permissions { get; set; } = new List<RolePermission>();
}
