using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Common;

/// <summary>Phase 5 accepts saved Creator Manual content without opening generation APIs to it.</summary>
public static class MediaStoryAccess
{
    public static void EnsureSupported(Story story)
    {
        if (story.Scope == StoryScope.Creator && story.Source == StorySource.Manual && !story.IsDeleted && story.ChildProfileId is null)
            return;
        GenerationStoryAccess.EnsureSupported(story);
    }

    public static async Task<bool> AuthorizeCreatorAsync(IUnitOfWork uow, Story story, int userId, CancellationToken ct)
    {
        EnsureSupported(story);
        if (story.Scope != StoryScope.Creator) return false;
        if (story.AuthorUserId != userId) throw new NotFoundException("Story", story.Id);
        var user = await uow.Repository<UserAccount>().FirstOrDefaultAsync(x => x.Id == userId, cancellationToken: ct);
        if (user is null || user.IsDeleted || user.Role != UserRole.ContentCreator || user.Status != AccountStatus.Active || user.LockedUntil > DateTime.UtcNow)
            throw new ForbiddenException();
        return true;
    }
}
