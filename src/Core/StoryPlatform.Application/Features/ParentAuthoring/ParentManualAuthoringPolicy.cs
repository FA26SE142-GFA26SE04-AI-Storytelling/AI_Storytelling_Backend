using System.Linq.Expressions;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ChildProfiles.Supervision.Interfaces;
using StoryPlatform.Application.Features.ManualAuthoring.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ParentAuthoring;

public sealed class ParentManualAuthoringPolicy(IUnitOfWork uow, ISupervisionAccessGuard guard) : IManualAuthoringPolicy
{
    public StoryScope Scope => StoryScope.Parent;
    public static readonly string[] Purposes = ["free", "family_memory", "understanding", "message"];

    public Expression<Func<Story, bool>> VisibleStories(int userId)
    {
        var children = uow.Repository<ChildProfile>().Query();
        var relationships = uow.Repository<SupervisionRelationship>().Query();
        var requests = uow.Repository<StoryGenerationRequest>().Query();
        var permissions = uow.Repository<SupervisionPermission>().Query();
        // Keep authorization inside the database predicate, before COUNT and pagination.
        return story => story.Scope == StoryScope.Parent && story.Source == StorySource.Manual &&
            story.CreateOperationId != null && story.CreateOperationId != "" &&
            !requests.Any(request => request.StoryId == story.Id) && story.AuthorUserId == userId && !story.IsDeleted &&
            children.Any(child => child.Id == story.ChildProfileId && !child.IsDeleted && child.Status == ChildProfileStatus.Active) &&
            relationships.Any(relationship => relationship.ChildProfileId == story.ChildProfileId &&
                relationship.SupervisorUserId == userId && !relationship.IsDeleted && relationship.RevokedAt == null &&
                (relationship.SupervisorRole == SupervisorRole.Owner ||
                 permissions.Any(permission => permission.SupervisionRelationshipId == relationship.Id &&
                     !permission.IsDeleted && permission.Permission == Permission.GenerateStory)));
    }

    public async Task<ChildProfile> RequireChildAsync(int user, int childId, CancellationToken ct)
    {
        if (childId <= 0) throw new BadRequestException("CHILD_REQUIRED");
        await guard.EnsurePermissionAsync(childId, user, Permission.GenerateStory, ct);
        var child = await uow.Repository<ChildProfile>().FirstOrDefaultAsync(x => x.Id == childId, cancellationToken: ct)
            ?? throw new NotFoundException("ChildProfile", childId);
        if (child.IsDeleted || child.Status != ChildProfileStatus.Active) throw new ConflictException("CHILD_NOT_ACTIVE");
        return child;
    }

    public async Task InitializeAsync(int userId, Story story, ManualDraftContext? context, CancellationToken ct)
    {
        if (context is null) throw new BadRequestException("CHILD_REQUIRED");
        var child = await RequireChildAsync(userId, context.ChildProfileId, ct);
        ValidatePurpose(context.Purpose);
        story.ChildProfileId = child.Id;
        story.WritingPurpose = context.Purpose ?? "free";
        if (string.IsNullOrWhiteSpace(story.Title)) story.Title = "Truyện của gia đình";
        var age = child.AgeBand == AgeBand.Age_9_10 ? "9-10" : "6-8";
        if (story.Language != child.Language || story.AgeBand != age)
            throw new BadRequestException("CHILD_CONTEXT_MISMATCH");
    }

    public async Task AuthorizeAsync(int userId, Story story, CancellationToken ct)
    {
        if (!StoryPlatform.Application.Common.ParentStoryContext.IsEditorStory(story) || story.AuthorUserId != userId || story.IsDeleted)
            throw new NotFoundException("Story", story.Id);
        if (await uow.Repository<StoryGenerationRequest>().ExistsAsync(x => x.StoryId == story.Id, ct))
            throw new ConflictException("AUTHORING_PIPELINE_CONFLICT");
        await RequireChildAsync(userId, story.ChildProfileId ?? 0, ct);
    }

    public Task ValidateAssistanceContextAsync(Story story, string? context, CancellationToken ct) =>
        new ParentAssistanceAccessPolicy(uow, guard).ValidateContextAsync(story, context, ct);

    public Task<bool> IsLockedAsync(int storyId, CancellationToken ct) =>
        uow.Repository<ParentStoryOperation>().ExistsAsync(x => x.StoryId == storyId &&
            x.Kind == "FamilyConfirmation" && !x.IsDeleted && x.RevokedAt == null, ct);

    public async Task EnsureWritableAsync(Story story, CancellationToken ct)
    {
        if (await IsLockedAsync(story.Id, ct)) throw new ConflictException("PARENT_STORY_FINALIZED");
    }

    public static void ValidatePurpose(string? purpose)
    {
        if (purpose is not null && !Purposes.Contains(purpose)) throw new BadRequestException("WRITING_PURPOSE_INVALID");
    }
}
