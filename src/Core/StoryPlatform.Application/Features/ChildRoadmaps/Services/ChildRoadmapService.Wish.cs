using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ChildRoadmaps.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ChildRoadmaps.Services;

/// <summary>Dùng truyện có sẵn cho điều ước.</summary>
public partial class ChildRoadmapService
{
    public Task<WishRoadmapResultDto> AddForWishAsync(
        int actorUserId,
        int childProfileId,
        int storyId,
        CancellationToken cancellationToken = default)
        => AddWishStoryAsync(actorUserId, childProfileId, storyId, null, cancellationToken);

    public Task<WishRoadmapResultDto> AddForWishAsync(int actorUserId, int childProfileId, int storyId,
        int wishId, CancellationToken cancellationToken = default)
        => AddWishStoryAsync(actorUserId, childProfileId, storyId, wishId, cancellationToken);

    private async Task<WishRoadmapResultDto> AddWishStoryAsync(int actorUserId, int childProfileId, int storyId,
        int? wishId, CancellationToken cancellationToken)
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
            StoryWish? wish = null;
            if (wishId.HasValue)
            {
                await _unitOfWork.AcquireTransactionLockAsync(-childProfileId, cancellationToken);
                wish = await _unitOfWork.Repository<StoryWish>().FirstOrDefaultAsync(
                    x => x.Id == wishId && !x.IsDeleted, cancellationToken: cancellationToken)
                    ?? throw new NotFoundException("StoryWish", wishId.Value);
                await _guard.EnsurePermissionAsync(childProfileId, actorUserId, Permission.ApproveStory, cancellationToken);
                if (wish.ChildProfileId != childProfileId || wish.Kind != StoryWishKind.Wish ||
                    wish.Status != StoryWishStatus.Pending || wish.GeneratedStoryId.HasValue || wish.GenerationRequestId.HasValue)
                    throw new ConflictException("WISH_ALREADY_HANDLED");
                if (!await _unitOfWork.Repository<StoryWishSuggestion>().ExistsAsync(
                    x => x.StoryWishId == wish.Id && x.StoryId == storyId && !x.IsDeleted && x.CommunitySubmissionId == null,
                    cancellationToken))
                    throw new BadRequestException("WISH_STORY_NOT_SUGGESTED");
            }
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

            if (wish is not null)
            {
                wish.Status = StoryWishStatus.Fulfilled;
                wish.FulfilledStoryId = storyId;
                wish.FulfilledWithExistingStory = true;
                wish.ResolvedByUserId = actorUserId;
                wish.ResolvedAt = DateTime.UtcNow;
                wish.UpdatedAt = wish.ResolvedAt;
                _unitOfWork.Repository<StoryWish>().Update(wish);
                await _auditLogWriter.LogAsync(actorUserId, "StoryWishFulfilledWithExistingStory", nameof(StoryWish), wish.Id,
                    new { status = StoryWishStatus.Pending.ToString() }, new { status = wish.Status.ToString(), storyId }, cancellationToken);
            }

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
            _unitOfWork.DiscardTrackedChanges();
            throw;
        }
    }
}
