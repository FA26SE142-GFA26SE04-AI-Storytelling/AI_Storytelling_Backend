using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.ChildRoadmaps.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ChildRoadmaps.Services;

/// <summary>
/// Mở truyện kế (UC-L4A-05) ở dạng rút gọn: chỉ kiểm hồ sơ hoạt động, Hold Mode và truyện còn đọc được.
/// Các kiểm tra chủ đề bị chặn, độ tuổi và độ dài (BR-L4A-07) thuộc Giai đoạn 4 và phải được thêm vào đây trước
/// khi có API tạo child_roadmap_items.
/// </summary>
public class ChildRoadmapAdvancer : IChildRoadmapAdvancer
{
    private readonly IUnitOfWork _unitOfWork;

    public ChildRoadmapAdvancer(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<ChildRoadmapItem?> AdvanceAsync(int childProfileId, CancellationToken cancellationToken = default)
    {
        var child = await _unitOfWork.Repository<ChildProfile>().GetByIdAsync(childProfileId, cancellationToken);
        if (child == null || child.IsDeleted || child.Status != ChildProfileStatus.Active)
        {
            return null;
        }

        var itemRepository = _unitOfWork.Repository<ChildRoadmapItem>();
        var steps = await itemRepository.FindAsync(
            item => item.ChildProfileId == childProfileId
                    && (item.Status == ChildRoadmapItemStatus.Waiting || item.Status == ChildRoadmapItemStatus.Open),
            cancellationToken: cancellationToken);

        var open = steps.FirstOrDefault(step => step.Status == ChildRoadmapItemStatus.Open);
        if (open != null)
        {
            return open;
        }

        // Hold Mode chưa được mở khóa thì không mở truyện kế (BR-L4A-10).
        var holdModeActive = await _unitOfWork.Repository<InterventionCase>().ExistsAsync(
            holdCase => holdCase.ChildProfileId == childProfileId
                        && !holdCase.IsDeleted
                        && holdCase.Status != InterventionStatus.ResolvedUnlocked,
            cancellationToken);
        if (holdModeActive)
        {
            return null;
        }

        var now = DateTime.UtcNow;
        var changed = false;
        ChildRoadmapItem? opened = null;
        foreach (var candidate in steps.OrderBy(step => step.Position).ThenBy(step => step.Id))
        {
            // FindAsync trả bản không theo dõi: lấy bản đang theo dõi trước khi sửa.
            var step = await itemRepository.GetByIdAsync(candidate.Id, cancellationToken);
            if (step == null || step.IsDeleted || step.Status != ChildRoadmapItemStatus.Waiting)
            {
                continue;
            }

            var story = await _unitOfWork.Repository<Story>().GetByIdAsync(step.StoryId, cancellationToken);
            var unavailable = ClassifyUnavailable(story);
            if (unavailable.HasValue)
            {
                step.Status = ChildRoadmapItemStatus.Skipped;
                step.SkipReason = unavailable.Value;
                step.SkippedAt = now;
                itemRepository.Update(step);
                changed = true;
                continue;
            }

            step.Status = ChildRoadmapItemStatus.Open;
            step.OpenedAt = now;
            step.ExpectedCompletionAt = step.ExpectedDays.HasValue ? now.AddDays(step.ExpectedDays.Value) : null;
            itemRepository.Update(step);
            opened = step;
            changed = true;
            break;
        }

        if (changed)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return opened;
    }

    /// <summary>Null khi truyện còn đọc được; ngược lại là lý do bỏ qua.</summary>
    private static ChildRoadmapSkipReason? ClassifyUnavailable(Story? story)
    {
        if (story == null || story.IsDeleted)
        {
            return ChildRoadmapSkipReason.SourceDeleted;
        }

        if (story.Status != StoryStatus.Ready || story.ArchivedAt != null)
        {
            return ChildRoadmapSkipReason.StoryUnavailable;
        }

        return null;
    }
}
