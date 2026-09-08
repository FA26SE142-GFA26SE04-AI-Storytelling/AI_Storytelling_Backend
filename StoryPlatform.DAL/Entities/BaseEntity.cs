using System;

namespace StoryPlatform.DAL.Entities;

/// <summary>
/// Lớp thực thể cơ sở chứa các trường kiểm toán (Audit fields) dùng chung cho toàn bộ Entity.
/// </summary>
public abstract class BaseEntity
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public bool IsDeleted { get; set; } = false;
}
