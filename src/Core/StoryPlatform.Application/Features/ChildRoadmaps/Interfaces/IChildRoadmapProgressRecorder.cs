using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Application.Features.ChildRoadmaps.Interfaces;

/// <summary>Bước lộ trình vừa hoàn thành và các huy hiệu vừa mở theo nó (ví dụ ROADMAP_FINISHER).</summary>
public sealed record RoadmapCompletionResult(ChildRoadmapItem Step, IReadOnlyList<string> NewBadgeCodes);

public interface IChildRoadmapProgressRecorder
{
    /// <summary>Null khi phiên không khớp bước nào của lộ trình (hoặc là đọc lại).</summary>
    Task<RoadmapCompletionResult?> RecordSessionCompletedAsync(
        int readingSessionId, bool isRetake, CancellationToken cancellationToken = default);
}
