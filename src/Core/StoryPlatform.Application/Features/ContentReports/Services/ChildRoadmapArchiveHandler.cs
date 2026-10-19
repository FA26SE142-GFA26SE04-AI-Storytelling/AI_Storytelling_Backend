using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.ChildRoadmaps.Interfaces;
using StoryPlatform.Application.Features.ContentReports.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ContentReports.Services;

/// <summary>
/// 3E: truyện bị Archive thì mọi bước đang chờ hoặc đang mở của truyện đó trong lộ trình của mọi trẻ bị bỏ qua
/// (story_unavailable); bước đang mở bị bỏ qua thì mở truyện kế ngay (WF-L4A UC-L4A-05). Phiên đọc đang dở không bị
/// dừng ở đây (việc đó thuộc 3B). Bước đã hoàn thành, bỏ qua hoặc gỡ giữ nguyên.
/// </summary>
public class ChildRoadmapArchiveHandler : IStoryArchiveHandler
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogWriter _auditLogWriter;
    private readonly IChildRoadmapAdvancer _advancer;

    public ChildRoadmapArchiveHandler(
        IUnitOfWork unitOfWork, IAuditLogWriter auditLogWriter, IChildRoadmapAdvancer advancer)
    {
        _unitOfWork = unitOfWork;
        _auditLogWriter = auditLogWriter;
        _advancer = advancer;
    }

    public async Task HandleAsync(StoryArchiveContext context, CancellationToken cancellationToken = default)
    {
        var storyId = context.Story.Id;
        var repository = _unitOfWork.Repository<ChildRoadmapItem>();
        var affected = await repository.FindAsync(
            item => item.StoryId == storyId
                    && (item.Status == ChildRoadmapItemStatus.Waiting || item.Status == ChildRoadmapItemStatus.Open),
            cancellationToken: cancellationToken);
        if (affected.Count == 0)
        {
            return;
        }

        var skipped = new List<(ChildRoadmapItem Item, ChildRoadmapItemStatus Before)>();
        foreach (var candidate in affected)
        {
            // FindAsync trả bản không theo dõi: lấy bản đang theo dõi trước khi sửa.
            var item = await repository.GetByIdAsync(candidate.Id, cancellationToken);
            if (item == null || item.IsDeleted
                || item.Status is not (ChildRoadmapItemStatus.Waiting or ChildRoadmapItemStatus.Open))
            {
                continue;
            }

            var before = item.Status;
            item.Status = ChildRoadmapItemStatus.Skipped;
            item.SkipReason = ChildRoadmapSkipReason.StoryUnavailable;
            item.SkippedAt = context.Now;
            repository.Update(item);
            skipped.Add((item, before));
        }

        if (skipped.Count == 0)
        {
            return;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        foreach (var (item, before) in skipped)
        {
            await _auditLogWriter.LogAsync(
                context.ActorUserId, "ChildRoadmapItemSkippedByArchive", nameof(ChildRoadmapItem), item.Id,
                beforeState: new { status = before.ToString() },
                afterState: new
                {
                    status = item.Status.ToString(),
                    skipReason = ChildRoadmapSkipReason.StoryUnavailable.ToString(),
                    childProfileId = item.ChildProfileId,
                    storyId
                },
                cancellationToken: cancellationToken);
        }

        // Trẻ có bước đang mở bị bỏ qua thì mở truyện kế ngay, không chờ trẻ đọc xong.
        var childrenToAdvance = skipped
            .Where(entry => entry.Before == ChildRoadmapItemStatus.Open)
            .Select(entry => entry.Item.ChildProfileId)
            .Distinct()
            .ToList();
        foreach (var childProfileId in childrenToAdvance)
        {
            await _advancer.AdvanceAsync(childProfileId, cancellationToken);
        }
    }
}
