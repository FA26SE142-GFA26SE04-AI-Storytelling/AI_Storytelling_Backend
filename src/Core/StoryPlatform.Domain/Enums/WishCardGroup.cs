namespace StoryPlatform.Domain.Enums;

/// <summary>
/// Nhóm thẻ ước (WF-L2D): 4 nhóm cho Bé ước và 3 nhóm chỉ cho Bé sáng tác. Lưu dạng chuỗi trong DB.
/// </summary>
public enum WishCardGroup
{
    Character = 1,
    Place = 2,
    Challenge = 3,
    Lesson = 4,
    /// <summary>Bé sáng tác — "Bạn ấy muốn gì?".</summary>
    Desire = 5,
    /// <summary>Bé sáng tác — "Chuyện gì xảy ra?".</summary>
    Event = 6,
    /// <summary>Bé sáng tác — "Câu chuyện kết thúc thế nào?". Chỉ kết thúc an toàn, tích cực.</summary>
    Ending = 7
}
