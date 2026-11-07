using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.Interfaces;
using StoryPlatform.Application.Features.ParentAuthoring;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Common;

/// <summary>Phase 5 accepts saved Creator Manual content without opening generation APIs to it.</summary>
public static class MediaStoryAccess
{
    public static void EnsureSupported(Story story)
    {
        if (ParentStoryContext.IsEditorStory(story) && !story.IsDeleted && story.ChildProfileId.HasValue)
            return;
        if (story.Scope == StoryScope.Creator && story.Source == StorySource.Manual && !story.IsDeleted && story.ChildProfileId is null)
            return;
        GenerationStoryAccess.EnsureSupported(story);
    }

    /// <summary>
    /// Chỉ tác giả được thao tác trên truyện: Creator là tác giả truyện Creator, phụ huynh là chủ truyện bản nháp thủ công
    /// hoặc chủ hồ sơ bé của truyện AI. Dùng chung cho gói media và ảnh bìa để hai nơi không lệch quy tắc.
    /// </summary>
    public static async Task AuthorizeAuthorAsync(IUnitOfWork uow, IChildProfileAccessGuard? supervision,
        Story story, int userId, CancellationToken ct)
    {
        EnsureSupported(story);
        if (await AuthorizeCreatorAsync(uow, story, userId, ct)) return;
        if (ParentStoryContext.IsEditorStory(story))
        {
            if (supervision is null) throw new ForbiddenException();
            await new ParentManualAuthoringPolicy(uow, supervision).AuthorizeAsync(userId, story, ct);
            return;
        }
        if (!await ChildOwnership.IsOwnerAsync(uow, story.ChildProfileId, userId, ct))
            throw new ForbiddenException();
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
