using StoryPlatform.Application.Features.Stories.DTOs;

namespace StoryPlatform.Application.Features.Stories.Interfaces;

public interface IStoryCoverService
{
    /// <summary>Chọn một ảnh minh họa của chính truyện làm ảnh bìa (hoặc gỡ ảnh bìa khi MediaAssetId = null).</summary>
    Task SetCoverAsync(int userId, int storyId, SetStoryCoverRequestDto request, CancellationToken cancellationToken = default);
}
