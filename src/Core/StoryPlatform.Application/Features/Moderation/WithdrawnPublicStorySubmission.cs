using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.Moderation;

/// <summary>Permits resubmission only; historical content and media stay immutable.</summary>
public static class WithdrawnPublicStorySubmission
{
    public static async Task<ModerationItem?> FindReusableAsync(IUnitOfWork uow, Story story, StoryVersion version, CancellationToken ct)
    {
        if (story.IsPublished || story.Status != StoryStatus.Ready || story.ArchivedAt != null
            || !version.IsCurrent || version.StoryId != story.Id || version.Moderation.ReviewStatus != VersionReviewStatus.None)
            return null;
        var history = await uow.Repository<ModerationItem>().FindAsync(x => !x.IsDeleted && x.StoryId == story.Id
            && x.StoryVersionId == version.Id && x.ItemType == ModerationItemType.PublicStoryReview, cancellationToken: ct);
        if (history.Count == 0 || history.Any(x => x.Status != ModerationItemStatus.Withdrawn
            || x.CreatedByUserId != story.AuthorUserId || x.WithdrawnAt == null)) return null;
        return history.OrderByDescending(x => x.Id).First();
    }
}
