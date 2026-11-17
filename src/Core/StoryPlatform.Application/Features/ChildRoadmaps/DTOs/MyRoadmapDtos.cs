namespace StoryPlatform.Application.Features.ChildRoadmaps;

/// <summary>Hằng số của màn hình "Bố mẹ chọn cho em".</summary>
public static class MyRoadmapPolicy
{
    public const int DoneLimit = 50;
}

/// <summary>Truyện đang mở trong mục "Bố mẹ chọn cho em".</summary>
public class MyRoadmapOpenStoryDto
{
    public int StoryId { get; set; }
    public string? Title { get; set; }
    public string? CoverImageUrl { get; set; }
    public string? Note { get; set; }

    /// <summary>Chỉ có giá trị khi ngày mong đọc xong vẫn còn ở tương lai.</summary>
    public DateTime? ExpectedBy { get; set; }
    public int Stage { get; set; }
}

/// <summary>Truyện chưa tới lượt trong lộ trình của trẻ.</summary>
public class MyRoadmapUpcomingDto
{
    /// <summary>Thứ tự chặng tính từ chặng kế (1 = chặng kế tiếp); truyện cùng chặng có cùng Order.</summary>
    public int Order { get; set; }
    public int StoryId { get; set; }
    public string? Title { get; set; }
    public string? CoverImageUrl { get; set; }
    public bool IsLocked { get; set; } = true;
}

public class MyRoadmapDoneDto
{
    public int StoryId { get; set; }
    public string? Title { get; set; }
    public string? CoverImageUrl { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int Stage { get; set; }
}

public class MyRoadmapDto
{
    /// <summary>Mọi truyện đang mở của chặng hôm nay.</summary>
    public List<MyRoadmapOpenStoryDto> OpenStories { get; set; } = new();
    public List<MyRoadmapUpcomingDto> Upcoming { get; set; } = new();
    public List<MyRoadmapDoneDto> Done { get; set; } = new();
    public int CompletedCount { get; set; }
    public int TotalCount { get; set; }
    public bool IsFinished { get; set; }
}
