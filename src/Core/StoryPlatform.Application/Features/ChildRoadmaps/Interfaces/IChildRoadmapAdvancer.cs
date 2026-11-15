using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Application.Features.ChildRoadmaps.Interfaces;

/// <summary>Mở truyện kế trong lộ trình của một trẻ (WF-L4A UC-L4A-05).</summary>
public interface IChildRoadmapAdvancer
{
    /// <summary>
    /// Trẻ còn truyện đang mở thì trả truyện mở có Id nhỏ nhất. Chưa có thì mở mọi truyện còn đọc được của chặng chờ
    /// nhỏ nhất (truyện không còn đọc được bị bỏ qua; cả chặng bị bỏ qua thì xét chặng sau), chỉ khi hồ sơ trẻ hoạt động.
    /// Trả null khi không mở được truyện nào.
    /// </summary>
    Task<ChildRoadmapItem?> AdvanceAsync(int childProfileId, CancellationToken cancellationToken = default);
}
