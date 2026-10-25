namespace StoryPlatform.Application.Features.WishCards;

public class AddWishCardMatchRequestDto
{
    public int WishCardId { get; set; }
}

public class WishCardMatchDto
{
    public int StoryId { get; set; }
    public int WishCardId { get; set; }
    public string Group { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    /// <summary>"Moderator" hoặc "Ai".</summary>
    public string Source { get; set; } = string.Empty;
}
