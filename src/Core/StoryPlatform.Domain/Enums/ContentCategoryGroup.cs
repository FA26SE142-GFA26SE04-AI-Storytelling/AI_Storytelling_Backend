namespace StoryPlatform.Domain.Enums;

/// <summary>
/// Nhóm hiển thị của nhãn nội dung. Lưu dạng chuỗi trong DB.
/// Chỉ dùng để phân loại/hiển thị; việc chặn theo tuổi vẫn dựa vào DefaultBlockedAgeBands.
/// </summary>
public enum ContentCategoryGroup
{
    /// <summary>Thể loại.</summary>
    Genre = 1,
    /// <summary>Giá trị và kỹ năng sống.</summary>
    LifeValue = 2,
    /// <summary>Học tập.</summary>
    Learning = 3,
    /// <summary>Cảnh báo an toàn.</summary>
    SafetyWarning = 4
}
