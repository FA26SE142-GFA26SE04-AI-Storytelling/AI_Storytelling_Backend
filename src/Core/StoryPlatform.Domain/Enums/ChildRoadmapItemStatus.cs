namespace StoryPlatform.Domain.Enums;

/// <summary>
/// Trạng thái một truyện trong lộ trình của trẻ (WF-L4A Mục 4). Lưu dạng chuỗi. Completed, Skipped, Removed là
/// trạng thái kết thúc.
/// </summary>
public enum ChildRoadmapItemStatus
{
    Waiting = 1,
    Open = 2,
    Completed = 3,
    Skipped = 4,
    Removed = 5
}
