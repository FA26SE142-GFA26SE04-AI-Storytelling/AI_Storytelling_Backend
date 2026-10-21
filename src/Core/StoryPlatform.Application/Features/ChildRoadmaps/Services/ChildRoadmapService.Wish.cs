using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ChildRoadmaps.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ChildRoadmaps.Services;

/// <summary>Dùng truyện có sẵn cho điều ước.</summary>
public partial class ChildRoadmapService
{
    public async Task<WishRoadmapResultDto> AddForWishAsync(
        int actorUserId,
        int childProfileId,
        int storyId,
        CancellationToken cancellationToken = default)
    {
        await _guard.EnsurePermissionAsync(
            childProfileId, actorUserId, Permission.ApproveStory, cancellationToken);

        var child = await _unitOfWork.Repository<ChildProfile>().GetByIdAsync(
            childProfileId, cancellationToken);
        if (child == null || child.IsDeleted)
        {
            throw new NotFoundException("Hồ sơ trẻ", childProfileId);
        }

        if (child.Status != ChildProfileStatus.Active)
        {
            throw new ConflictException("Hồ sơ của bé đang không hoạt động nên chưa thêm được truyện.");
        }

        var story = await _unitOfWork.Repository<Story>().GetByIdAsync(storyId, cancellationToken);
        if (story == null || story.IsDeleted)
        {
            throw new NotFoundException("Truyện", storyId);
        }

        if (story.Scope == StoryScope.Parent && story.ChildProfileId != childProfileId)
        {
            throw new BadRequestException("Truyện riêng của bé khác không dùng được cho điều ước này.");
        }

        var unsuitable = await _eligibility.EvaluateAsync(child, story, cancellationToken);
        if (unsuitable.HasValue)
        {
            throw new BadRequestException(MessageFor(SkipCodeFor(unsuitable.Value)));
        }

        var repository = _unitOfWork.Repository<ChildRoadmapItem>();
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var inserted = (await _inserter.InsertAsync(new InsertRequest(
                childProfileId,
                actorUserId,
                new[] { new NewRoadmapStep(storyId, null, ChildRoadmapPolicy.WishNote) },
                RoadmapInsertPosition.NextUp,
                ChildRoadmapItemSource.Wish,
                null,
                MoveExistingToNextUp: true), cancellationToken)).Single();

            if (!inserted.Created)
            {
                var tracked = await repository.GetByIdAsync(inserted.ItemId, cancellationToken);
                tracked!.Source = ChildRoadmapItemSource.Wish;
                tracked.Note = string.IsNullOrWhiteSpace(tracked.Note)
                    ? ChildRoadmapPolicy.WishNote
                    : tracked.Note;
                repository.Update(tracked);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            await _advancer.AdvanceAsync(childProfileId, cancellationToken);

            var outcome = inserted.Created ? "Added" : inserted.Moved ? "MovedToNext" : "Tagged";
            await _auditLogWriter.LogAsync(
                actorUserId,
                "ChildRoadmapWishStoryAdded",
                nameof(ChildRoadmapItem),
                inserted.ItemId,
                beforeState: null,
                afterState: new { childProfileId, storyId, outcome },
                cancellationToken: cancellationToken);

            await _notifier.NotifyOtherSupervisorsAsync(
                childProfileId,
                actorUserId,
                NotificationType.RoadmapStoryAdded,
                new { childProfileId, storyId, wish = true },
                cancellationToken);

            await _unitOfWork.CommitTransactionAsync(cancellationToken);
            return new WishRoadmapResultDto { ItemId = inserted.ItemId, Outcome = outcome };
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            throw;
        }
    }
}
