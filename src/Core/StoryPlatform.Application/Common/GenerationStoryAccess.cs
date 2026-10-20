using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AIStoryInput.Models;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Common;

/// <summary>Creator generation is owned by an active author, never by a borrowed child profile.</summary>
public static class GenerationStoryAccess
{
    public static void EnsureSupported(Story story)
    {
        if (ParentStoryContext.IsEditorStory(story)) throw new ConflictException("PARENT_MANUAL_USE_AUTHORING_API");
        if (story.IsDeleted || (story.Scope != StoryScope.Parent &&
            (story.Scope != StoryScope.Creator || story.Source != StorySource.Ai || story.ChildProfileId != null)))
            throw new NotFoundException("Story", story.Id);
    }

    public static async Task<bool> AuthorizeCreatorAsync(IUnitOfWork uow, Story story, int userId, CancellationToken ct)
    {
        EnsureSupported(story);
        if (story.Scope != StoryScope.Creator) return false;
        if (story.AuthorUserId != userId) throw new NotFoundException("Story", story.Id);
        var user = await uow.Repository<UserAccount>().GetByIdAsync(userId, ct);
        if (user is null || user.IsDeleted || user.Role != UserRole.ContentCreator || user.Status != AccountStatus.Active ||
            user.LockedUntil > DateTime.UtcNow)
            throw new ForbiddenException();
        return true;
    }

    public static void ValidateSnapshot(Story story, AIStoryInputContextSnapshot context)
    {
        EnsureSupported(story);
        if (story.Scope == StoryScope.Creator)
        {
            if (context.AudienceScope != "Creator" || context.CreatorUserId != story.AuthorUserId ||
                context.ChildProfileId != null || context.RequiredApprovalMode != "always_manual")
                throw new InvalidOperationException("CREATOR_CONTEXT_INVALID");
            return;
        }
        if (context.AudienceScope is not (null or "Parent") || !context.ConsentRecordedAt.HasValue || context.ConsentPolicyVersion <= 0)
            throw new InvalidOperationException("CONSENT_REQUIRED");
    }
}
