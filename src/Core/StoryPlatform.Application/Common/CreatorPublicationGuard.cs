using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Common;

/// <summary>Submitted versions are immutable, including after withdrawal or a moderation decision.</summary>
public static class CreatorPublicationGuard
{
    // FirstOrDefaultAsync is intentionally a no-tracking database read in GenericRepository.
    // Call under the story transaction lock when used before a write.
    public static async Task EnsureWritableAsync(IUnitOfWork uow, int storyId, int? versionId, CancellationToken ct)
    {
        var story = await uow.Repository<Story>().FirstOrDefaultAsync(x => x.Id == storyId, cancellationToken: ct)
            ?? throw new NotFoundException("Story", storyId);
        if (story.Scope != StoryScope.Creator) return;
        if (story.IsDeleted || story.IsPublished || story.ArchivedAt != null || story.Status == StoryStatus.Rejected)
            throw new ConflictException("CREATOR_PUBLICATION_LOCKED");
        if (await uow.Repository<ModerationItem>().ExistsAsync(x => x.StoryId == storyId && !x.IsDeleted &&
            (x.ItemType == ModerationItemType.PublicStoryReview || x.ItemType == ModerationItemType.PublicStoryRemoval) &&
            (x.Status == ModerationItemStatus.Pending || x.Status == ModerationItemStatus.Claimed), ct))
            throw new ConflictException("CREATOR_PUBLICATION_LOCKED");
        var version = await uow.Repository<StoryVersion>().FirstOrDefaultAsync(x => x.StoryId == storyId &&
            (versionId.HasValue ? x.Id == versionId.Value : x.IsCurrent), cancellationToken: ct);
        if (versionId.HasValue && version == null) throw new ConflictException("STALE_STORY_VERSION");
        if (version != null && (!version.IsCurrent || version.Moderation.ReviewStatus != VersionReviewStatus.None ||
            version.Moderation.ReviewSubmittedAt != null || await uow.Repository<ModerationItem>().ExistsAsync(x =>
                x.ItemType == ModerationItemType.PublicStoryReview && x.StoryVersionId == version.Id, ct)))
            throw new ConflictException("SUBMITTED_VERSION_IMMUTABLE_CREATE_REVISION");
    }

    public static async Task<T> WriteAsync<T>(IUnitOfWork uow, int storyId, int? versionId,
        Func<Task<T>> action, CancellationToken ct)
    {
        var creator = await uow.Repository<Story>().ExistsAsync(x => x.Id == storyId && x.Scope == StoryScope.Creator, ct);
        if (!creator) return await action();
        await uow.BeginTransactionAsync(ct);
        try
        {
            await uow.AcquireTransactionLockAsync(storyId, ct);
            await EnsureWritableAsync(uow, storyId, versionId, ct);
            var result = await action();
            await uow.CommitTransactionAsync(ct);
            return result;
        }
        catch { await uow.RollbackTransactionAsync(CancellationToken.None); throw; }
    }
}
