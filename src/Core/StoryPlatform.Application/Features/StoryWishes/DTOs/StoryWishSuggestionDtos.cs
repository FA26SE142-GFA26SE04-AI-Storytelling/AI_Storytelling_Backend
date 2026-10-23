using StoryPlatform.Application.Features.WishCards;

namespace StoryPlatform.Application.Features.StoryWishes;

/// <summary>Một truyện gợi ý như trẻ thấy. Mở bằng StoryId, hoặc CommunitySubmissionId nếu là truyện Cộng đồng.</summary>
public class WishSuggestionDto
{
    public int Rank { get; set; }
    public int? StoryId { get; set; }
    public int? CommunitySubmissionId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? CoverImageUrl { get; set; }
    /// <summary>Các thẻ trùng khớp, để app vẽ biểu tượng.</summary>
    public List<ChildWishCardDto> MatchedCards { get; set; } = new();
}

/// <summary>Một truyện gợi ý như phụ huynh thấy: thêm trạng thái đọc của con.</summary>
public class ParentWishSuggestionDto : WishSuggestionDto
{
    public bool Opened { get; set; }
    public bool Completed { get; set; }
    public string? Feeling { get; set; }
    public string? FeelingLabel { get; set; }
    /// <summary>"Bé đã đọc … và rất thích — …" khi con đọc xong và chọn rất thích / thích.</summary>
    public string? Hint { get; set; }
}

public class ParentWishSuggestionsDto
{
    public int WishId { get; set; }
    public List<ParentWishSuggestionDto> Items { get; set; } = new();
    /// <summary>"Chưa có truyện phù hợp trong thư viện" khi không có gợi ý nào.</summary>
    public string? EmptyMessage { get; set; }
}
