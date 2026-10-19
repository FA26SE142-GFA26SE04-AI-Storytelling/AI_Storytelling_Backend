using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Application.Features.ContentReports.Interfaces;

/// <summary>Truyện người báo cáo truy cập được, kèm phiên bản trẻ đang đọc nếu xác định được.</summary>
public sealed record ReportableStory(Story Story, int? DisplayedVersionId);

/// <summary>Kiểm tra người giám sát có thể truy cập truyện qua một trẻ đang hoạt động hay không.</summary>
public interface IReportableStoryResolver
{
    Task<ReportableStory> ResolveAsync(
        int reporterUserId, int storyId, CancellationToken cancellationToken = default);
}
