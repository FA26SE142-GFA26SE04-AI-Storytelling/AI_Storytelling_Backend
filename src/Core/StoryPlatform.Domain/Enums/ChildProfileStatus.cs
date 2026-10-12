namespace StoryPlatform.Domain.Enums;

/// <summary>
/// Trạng thái hồ sơ trẻ — Luồng 1 Mục 7. Lưu dạng chuỗi trong DB nên
/// giá trị số chỉ dùng để sắp xếp trong code.
/// </summary>
public enum ChildProfileStatus
{
    Draft = 1,
    Active = 2,
    Suspended = 3,
    Archived = 4,
    Deleted = 5
}
