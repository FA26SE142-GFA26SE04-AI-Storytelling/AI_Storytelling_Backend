using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.StoryWishes.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.StoryWishes.Services;

/// <summary>Các mốc thời gian của điều ước (WF-L2D UC-L2D-04, bảng "Vòng đời điều ước"). Chạy bởi StoryWishSweepWorker.</summary>
public class StoryWishSweepService : IStoryWishSweepService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IStoryWishNotifier _notifier;
    private readonly TimeProvider _clock;

    public StoryWishSweepService(IUnitOfWork unitOfWork, IStoryWishNotifier notifier, TimeProvider clock)
    {
        _unitOfWork = unitOfWork;
        _notifier = notifier;
        _clock = clock;
    }

    public async Task<StoryWishSweepResult> SweepAsync(CancellationToken ct = default)
    {
        var candidates = await _unitOfWork.Repository<StoryWish>().FindAsync(
            x => !x.IsDeleted && (x.Status == StoryWishStatus.Draft || x.Status == StoryWishStatus.Pending || x.Status == StoryWishStatus.Accepted),
            cancellationToken: ct);
        var totals = new int[6];
        foreach (var childId in candidates.Select(x => x.ChildProfileId).Distinct().OrderBy(x => x))
        {
            var prepared = new List<PreparedStoryWishNotification>();
            await _unitOfWork.BeginTransactionAsync(ct);
            try
            {
                // Match submission, generation and Parent decision locks; discard the pre-lock status snapshot.
                await _unitOfWork.AcquireTransactionLockAsync(-childId, ct);
                var result = await SweepChildAsync(childId, prepared, ct);
                await _unitOfWork.CommitTransactionAsync(ct);
                totals[0] += result.DraftsDeleted;
                totals[1] += result.Cancelled;
                totals[2] += result.Warned;
                totals[3] += result.Expired;
                totals[4] += result.Fulfilled;
                totals[5] += result.Declined;
            }
            catch
            {
                await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
                _unitOfWork.DiscardTrackedChanges();
                throw;
            }
            // The transaction and its rollback handler have ended before any external delivery.
            if (prepared.Count > 0) await _notifier.PublishPreparedAsync(prepared, ct);
        }
        return new StoryWishSweepResult(totals[0], totals[1], totals[2], totals[3], totals[4], totals[5]);
    }

    private async Task<StoryWishSweepResult> SweepChildAsync(int childId, List<PreparedStoryWishNotification> prepared, CancellationToken ct)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var repository = _unitOfWork.Repository<StoryWish>();
        int draftsDeleted = 0, cancelled = 0, warned = 0, expired = 0, fulfilled = 0, declined = 0;

        var live = (await repository.FindAsync(
                wish => wish.ChildProfileId == childId && !wish.IsDeleted
                        && (wish.Status == StoryWishStatus.Draft
                            || wish.Status == StoryWishStatus.Pending
                            || wish.Status == StoryWishStatus.Accepted),
                cancellationToken: ct))
            .ToList();

        // 1–2. Hồ sơ trẻ không còn Active → hủy điều ước đang chờ / đã nhận, xóa bản nháp.
        var childIds = live.Select(wish => wish.ChildProfileId).Distinct().ToList();
        var activeChildren = (await _unitOfWork.Repository<ChildProfile>().FindAsync(
                child => childIds.Contains(child.Id) && !child.IsDeleted && child.Status == ChildProfileStatus.Active,
                cancellationToken: ct))
            .Select(child => child.Id).ToHashSet();
        var draftCutoff = now - StoryWishPolicy.DraftLifetime;
        var remaining = new List<StoryWish>();
        foreach (var wish in live)
        {
            var childActive = activeChildren.Contains(wish.ChildProfileId);
            if (wish.Status == StoryWishStatus.Draft)
            {
                if (!childActive || wish.CreatedAt <= draftCutoff)
                {
                    repository.Delete(wish);
                    draftsDeleted++;
                }

                continue;
            }

            if (!childActive)
            {
                Close(wish, StoryWishStatus.Cancelled, StoryWishDecisionPolicy.NoteProfileInactive, now);
                cancelled++;
                continue;
            }

            remaining.Add(wish);
        }

        // 3–4. Pending: nhắc ngày 6 (một lần), hết hạn ngày 7.
        foreach (var wish in remaining.Where(wish => wish.Status == StoryWishStatus.Pending))
        {
            var since = wish.PendingSince ?? wish.SubmittedAt ?? wish.CreatedAt;
            if (now - since >= StoryWishDecisionPolicy.PendingLifetime)
            {
                Close(wish, StoryWishStatus.Expired, StoryWishDecisionPolicy.NoteExpired, now);
                expired++;
            }
            else if (now - since >= StoryWishDecisionPolicy.ExpiryWarningAfter && wish.ExpiryWarnedAt == null)
            {
                prepared.AddRange(await _notifier.PrepareExpiringAsync(wish, ct));
                wish.ExpiryWarnedAt = now;
                wish.UpdatedAt = now;
                repository.Update(wish);
                warned++;
            }
        }

        // 5. Accepted: truyện mới ready → Fulfilled; bị lưu trữ / mất → Declined; bị từ chối 7 ngày → Declined.
        var accepted = remaining.Where(wish => wish.Status == StoryWishStatus.Accepted && wish.GeneratedStoryId.HasValue).ToList();
        var storyIds = accepted.Select(wish => wish.GeneratedStoryId!.Value).Distinct().ToList();
        var stories = (await _unitOfWork.Repository<Story>().FindAsync(
            story => storyIds.Contains(story.Id), cancellationToken: ct)).ToDictionary(story => story.Id);
        foreach (var wish in accepted)
        {
            if (!stories.TryGetValue(wish.GeneratedStoryId!.Value, out var story)
                || story.IsDeleted
                || story.ArchivedAt != null
                || story.Status == StoryStatus.Archived)
            {
                Close(wish, StoryWishStatus.Declined, StoryWishDecisionPolicy.NoteStoryArchived, now);
                declined++;
            }
            else if (story.Status == StoryStatus.Ready)
            {
                wish.FulfilledStoryId = story.Id;
                wish.FulfilledWithExistingStory = false;
                Close(wish, StoryWishStatus.Fulfilled, null, now);
                fulfilled++;
            }
            else if (story.Status == StoryStatus.Rejected
                     && now - (story.UpdatedAt ?? story.CreatedAt) >= StoryWishDecisionPolicy.RejectedStoryGrace)
            {
                Close(wish, StoryWishStatus.Declined, StoryWishDecisionPolicy.NoteStoryRejected, now);
                declined++;
            }
        }

        await _unitOfWork.SaveChangesAsync(ct);
        return new StoryWishSweepResult(draftsDeleted, cancelled, warned, expired, fulfilled, declined);
    }

    private void Close(StoryWish wish, StoryWishStatus status, string? note, DateTime now)
    {
        wish.Status = status;
        wish.ResolutionNote = note;
        wish.ResolvedAt = now;
        wish.UpdatedAt = now;
        _unitOfWork.Repository<StoryWish>().Update(wish);
    }
}
