using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Application.Features.ChildRoadmaps.Interfaces;

/// <summary>Mở truyện kế trong lộ trình của một trẻ (WF-L4A UC-L4A-05).</summary>
public interface IChildRoadmapAdvancer
{
    /// <summary>
    /// Trẻ có bước đang mở thì trả bước đó. Chưa có thì mở bước chờ đầu tiên còn đọc được (bước không còn đọc được
    /// bị bỏ qua), chỉ khi hồ sơ trẻ hoạt động và trẻ không ở Hold Mode. Trả null khi không mở được bước nào.
    /// </summary>
    Task<ChildRoadmapItem?> AdvanceAsync(int childProfileId, CancellationToken cancellationToken = default);
}
