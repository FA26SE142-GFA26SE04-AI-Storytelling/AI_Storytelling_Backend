using StoryPlatform.Application.Features.AIStoryInput.DTOs;
using StoryPlatform.Application.Features.WishCards;

namespace StoryPlatform.Application.Features.StoryWishes;

/// <summary>Một điều ước như phụ huynh thấy (có lý do từ chối và ghi chú hệ thống — trẻ không bao giờ thấy).</summary>
public class ParentWishDto
{
    public int WishId { get; set; }
    public int ChildProfileId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public List<ChildWishCardDto> Cards { get; set; } = new();
    public bool LessonLeftToParent { get; set; }
    public DateTime? SubmittedAt { get; set; }
    /// <summary>Hạn xử lý (chỉ khi đang Pending).</summary>
    public DateTime? ExpiresAt { get; set; }
    public string? DeclineReason { get; set; }
    public string? ResolutionNote { get; set; }
    public int? GeneratedStoryId { get; set; }
    public int? FulfilledStoryId { get; set; }
    public bool FulfilledWithExistingStory { get; set; }
    public DateTime? ResolvedAt { get; set; }
}

public class ParentWishDetailDto
{
    public ParentWishDto Wish { get; set; } = new();
    public ParentWishSuggestionsDto Suggestions { get; set; } = new();
}

public class ParentWishGenerationFormDto
{
    public int WishId { get; set; }
    /// <summary>Con chọn "Để bố mẹ chọn" bài học: phụ huynh phải điền Lesson trước khi gửi.</summary>
    public bool LessonLeftToParent { get; set; }
    /// <summary>Form điền sẵn; phụ huynh sửa rồi gửi lại nguyên đối tượng này tới create-story.</summary>
    public SubmitAIStoryInputRequestDto Request { get; set; } = new();
}

public class ParentWishCreateStoryResultDto
{
    public ParentWishDto Wish { get; set; } = new();
    /// <summary>Tiến trình sinh truyện như luồng WF-L2A trả về.</summary>
    public AIStoryInputProgressDto Generation { get; set; } = new();
}

public class UseExistingStoryRequestDto
{
    /// <summary>Thứ tự 1–3 của truyện trong danh sách gợi ý của điều ước.</summary>
    public int SuggestionRank { get; set; }
}

public class DeclineWishRequestDto
{
    /// <summary>Tùy chọn, tối đa 500 ký tự; chỉ phụ huynh thấy.</summary>
    public string? Reason { get; set; }
}
