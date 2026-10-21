namespace StoryPlatform.Domain.Enums;

/// <summary>Lý do hệ thống bỏ qua một truyện trong lộ trình (WF-L4A Mục 4). Trẻ không thấy lý do này.</summary>
public enum ChildRoadmapSkipReason
{
    BlockedTopic = 1,
    WrongAge = 2,
    TooLong = 3,
    StoryUnavailable = 4,
    SourceDeleted = 5,
    DataDeleted = 6
}
