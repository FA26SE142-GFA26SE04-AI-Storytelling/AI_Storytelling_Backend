using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ChildRoadmaps.Services;

/// <summary>
/// Quy tắc số chặng của lộ trình (spec 2026-11-14 mục 3): Position = số chặng; các truyện Open cùng chặng S;
/// Completed ở chặng ≤ S; các chặng chờ liên tiếp S+1, S+2… Skipped/Removed không tham gia đánh số.
/// </summary>
public static class RoadmapStages
{
    public static int Current(IEnumerable<ChildRoadmapItem> items)
    {
        var list = items.ToList();
        var open = list.Where(item => item.Status == ChildRoadmapItemStatus.Open).Select(item => item.Position).ToList();
        if (open.Count > 0)
        {
            return open.Max();
        }

        var done = list.Where(item => item.Status == ChildRoadmapItemStatus.Completed).Select(item => item.Position).ToList();
        return done.Count > 0 ? done.Max() : 0;
    }

    /// <summary>Các chặng chờ theo thứ tự Position; trong một chặng xếp theo Id.</summary>
    public static List<List<ChildRoadmapItem>> WaitingStages(IEnumerable<ChildRoadmapItem> items) =>
        items.Where(item => item.Status == ChildRoadmapItemStatus.Waiting)
            .GroupBy(item => item.Position)
            .OrderBy(group => group.Key)
            .Select(group => group.OrderBy(item => item.Id).ToList())
            .ToList();

    /// <summary>
    /// Đánh số lại các chặng chờ thành S+1, S+2… Gọi sau SaveChanges vì FindAsync đọc bản không theo dõi.
    /// </summary>
    public static async Task RenumberWaitingAsync(
        IUnitOfWork unitOfWork, int childProfileId, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<ChildRoadmapItem>();
        var live = await repository.FindAsync(
            item => item.ChildProfileId == childProfileId
                    && (item.Status == ChildRoadmapItemStatus.Waiting || item.Status == ChildRoadmapItemStatus.Open
                        || item.Status == ChildRoadmapItemStatus.Completed),
            cancellationToken: cancellationToken);
        var number = Current(live);
        var changed = false;
        foreach (var stage in WaitingStages(live))
        {
            number += 1;
            var stageNumber = number;
            foreach (var candidate in stage.Where(item => item.Position != stageNumber).ToList())
            {
                var tracked = await repository.GetByIdAsync(candidate.Id, cancellationToken);
                tracked!.Position = stageNumber;
                repository.Update(tracked);
                changed = true;
            }
        }

        if (changed)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}
