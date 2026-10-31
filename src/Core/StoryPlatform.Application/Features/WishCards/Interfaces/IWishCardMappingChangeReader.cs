namespace StoryPlatform.Application.Features.WishCards.Interfaces;

public interface IWishCardMappingChangeReader
{
    Task<int> ReadWatermarkAsync(CancellationToken ct);
    Task<bool> HasChangesAfterAsync(int storyId, int watermark, CancellationToken ct);
}
