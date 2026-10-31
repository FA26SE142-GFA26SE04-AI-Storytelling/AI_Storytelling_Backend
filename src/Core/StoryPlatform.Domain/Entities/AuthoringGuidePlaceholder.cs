namespace StoryPlatform.Domain.Entities;

/// <summary>Một chỗ trống <c>{{Key}}</c> trong mẫu câu, kèm nhãn và gợi ý cho Parent.</summary>
public class AuthoringGuidePlaceholder : BaseEntity
{
    public int ItemId { get; set; }
    public string Key { get; set; } = string.Empty;

    /// <summary>Nhãn ô nhập; dữ liệu chuyển từ JSON cũ có thể chưa có.</summary>
    public string? Label { get; set; }

    public string? Hint { get; set; }
    public int Order { get; set; }
}
