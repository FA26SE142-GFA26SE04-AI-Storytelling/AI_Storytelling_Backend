using StoryPlatform.Domain.Entities.Enums;

namespace StoryPlatform.Domain.Entities;

/// <summary>
/// Thực thể Câu chuyện dành cho trẻ em (AI generated hoặc Manual sáng tác).
/// </summary>
public class Story : BaseEntity
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Content { get; set; }
    public string? CoverImageUrl { get; set; }
    public string? Genre { get; set; }
    public string? MoralLesson { get; set; }
    public string AgeBand { get; set; } = "6-8"; // e.g., 6-8, 9-12
    public string Language { get; set; } = "vi";
    public StorySource Source { get; set; } = StorySource.Ai;
    public StoryStatus Status { get; set; } = StoryStatus.Draft;
    public bool IsPublished { get; set; } = false;

    // Khóa ngoại tác giả / người khởi tạo
    public int AuthorUserId { get; set; }
    public virtual UserAccount? Author { get; set; }
}
