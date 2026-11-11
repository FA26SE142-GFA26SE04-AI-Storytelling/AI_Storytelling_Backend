using StoryPlatform.Application.Features.Moderation.DTOs;

namespace StoryPlatform.Application.Features.Moderation.Interfaces;

/// <summary>Đọc (không ghi) truyện đang được dùng ở đâu, cho Moderator khi duyệt gỡ / công khai lại.</summary>
public interface IStoryUsageImpactReader
{
    /// <param name="checkRepublish">true với mục PublicStoryReview: bật cờ WishCardsNeedReview khi truyện từng công khai và đang có thẻ.</param>
    /// <returns>null khi truyện không tồn tại hoặc đã xóa.</returns>
    Task<StoryImpactDto?> ReadAsync(int storyId, bool checkRepublish, CancellationToken cancellationToken = default);
}
