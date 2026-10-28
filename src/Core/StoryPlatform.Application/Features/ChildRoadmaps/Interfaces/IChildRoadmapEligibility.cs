using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ChildRoadmaps.Interfaces;

/// <summary>Truyện có hợp lệ để vào hoặc được mở trong lộ trình của một trẻ tại thời điểm này không.</summary>
public interface IChildRoadmapEligibility
{
    /// <summary>Null khi hợp lệ; ngược lại là lý do không hợp lệ.</summary>
    Task<ChildRoadmapSkipReason?> EvaluateAsync(
        ChildProfile child, Story? story, CancellationToken cancellationToken = default);

    /// <summary>Age is advisory when an authorized supervisor explicitly selects a story.</summary>
    Task<ChildRoadmapSkipReason?> EvaluateForParentSelectionAsync(
        ChildProfile child, Story? story, CancellationToken cancellationToken = default);
}
