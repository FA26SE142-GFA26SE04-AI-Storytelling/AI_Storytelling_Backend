using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.ChildRoadmaps.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ChildRoadmaps.Services;

/// <summary>
/// Mở truyện kế (UC-L4A-05): kiểm hồ sơ hoạt động, Hold Mode và tính hợp lệ của truyện
/// (BR-L4A-07) bằng IChildRoadmapEligibility.
/// </summary>
public class ChildRoadmapAdvancer : IChildRoadmapAdvancer
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IChildRoadmapEligibility _eligibility;

    public ChildRoadmapAdvancer(IUnitOfWork unitOfWork, IChildRoadmapEligibility eligibility)
    {
        _unitOfWork = unitOfWork;
        _eligibility = eligibility;
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
            var unavailable = await _eligibility.EvaluateAsync(child, story, cancellationToken);
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
}
