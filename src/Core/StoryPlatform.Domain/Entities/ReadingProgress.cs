namespace StoryPlatform.Domain.Entities;

public class ReadingProgress : BaseEntity
{
    public int ChildProfileId { get; set; }
    public virtual ChildProfile? ChildProfile { get; set; }

    public int StoryId { get; set; }
    public virtual Story? Story { get; set; }

    public int LastPageRead { get; set; } = 1;
    /// <summary>Chế độ đọc lần gần nhất (tên enum ReadingMode); null = chưa lưu.</summary>
    public string? LastReadingMode { get; set; }
    public bool IsFavorited { get; set; } = false;
    public bool IsBookmarked { get; set; } = false;
}
