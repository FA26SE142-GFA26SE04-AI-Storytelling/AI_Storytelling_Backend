using StoryPlatform.Application.Features.WishCards;

namespace StoryPlatform.Application.Features.StoryWishes;

public class SelectWishCardRequestDto
{
    public string? Group { get; set; }
    public int? CardId { get; set; }
    public bool LeaveToParent { get; set; }
}

public class WishDraftDto
{
    public int WishId { get; set; }
    public bool Resumed { get; set; }
    public DateTime ExpiresAt { get; set; }
    public List<ChildWishCardDto> Cards { get; set; } = new();
    public bool LessonLeftToParent { get; set; }
    public bool CanSubmit { get; set; }
}

public class ChildWishDto
{
    /// <summary>Truyện có sẵn để đọc trong lúc chờ (tối đa 3); rỗng khi không có, khi Hold Mode, hoặc khi đã xử lý xong.</summary>
    public List<WishSuggestionDto> Suggestions { get; set; } = new();

    public int WishId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public List<ChildWishCardDto> Cards { get; set; } = new();
    public bool LessonLeftToParent { get; set; }
    public int? FulfilledStoryId { get; set; }
    public DateTime? SubmittedAt { get; set; }
}
