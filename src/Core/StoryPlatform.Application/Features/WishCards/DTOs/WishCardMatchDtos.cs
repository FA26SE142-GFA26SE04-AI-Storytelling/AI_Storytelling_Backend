namespace StoryPlatform.Application.Features.WishCards;

public sealed class MatchStoryWishCardsRequestDto
{
    [System.ComponentModel.DataAnnotations.Range(1, int.MaxValue)]
    public int StoryVersionId { get; init; }
}

public sealed record StoryWishCardAiMatchResult(int StoryId, int StoryVersionId, int AddedCount,
    IReadOnlyList<StoryPlatform.Contracts.AI.WishCards.WishCardEvidence> Matches, string SourceFingerprint);

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
