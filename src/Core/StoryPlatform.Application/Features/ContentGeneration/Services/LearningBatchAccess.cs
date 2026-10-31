using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.Safety;
using StoryPlatform.Application.Features.ParentAuthoring;
using StoryPlatform.Application.Features.Moderation;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ContentGeneration.Services;

public static class LearningBatchAccess
{
    public static string Hash(object value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
    public static string Source(Story story, StoryVersion version) => Hash(new { StoryId = story.Id, VersionId = version.Id, version.Title, version.Content,
        version.Lesson, story.AgeBand, story.ReadingLevel, story.VocabularyLevel, story.Language, story.OutputMode });

    public static async Task<string> AuthorizeAsync(IUnitOfWork uow, IChildProfileAccessGuard guard, Story story, StoryVersion version, int actor, CancellationToken ct)
    {
        if (story.IsDeleted || version.IsDeleted || !version.IsCurrent || version.StoryId != story.Id || string.IsNullOrWhiteSpace(version.Content) ||
            story.OutputMode != StoryOutputMode.Learning || story.Status is not (StoryStatus.Draft or StoryStatus.OutlineReview or StoryStatus.ContentReview))
            throw new ConflictException("STALE_LEARNING_SOURCE");
        await CreatorPublicationGuard.EnsureWritableAsync(uow, story.Id, version.Id, ct);
        GrayZoneGate.EnsureCanContinue(story, version);
        if (story.Scope == StoryScope.Creator)
        {
            await MediaStoryAccess.AuthorizeCreatorAsync(uow, story, actor, ct);
            return Hash(new { story.AuthorUserId, story.AgeBand });
        }
        var user = await uow.Repository<UserAccount>().FirstOrDefaultAsync(x => x.Id == actor && !x.IsDeleted, cancellationToken: ct);
        if (user?.Role != UserRole.User || user.Status != AccountStatus.Active || user.LockedUntil > DateTime.UtcNow || story.ChildProfileId is not int childId)
            throw new ForbiddenException();
        await guard.EnsureOwnerAsync(childId, actor, ct);
        var child = await uow.Repository<ChildProfile>().FirstOrDefaultAsync(x => x.Id == childId && !x.IsDeleted, cancellationToken: ct);
        var policy = await uow.Repository<SafetyPolicy>().FirstOrDefaultAsync(x => x.ChildProfileId == childId && !x.IsDeleted, cancellationToken: ct);
        if (child?.Status != ChildProfileStatus.Active || policy?.ConsentRecorded != true || !policy.ConsentRecordedAt.HasValue || ConsentPolicy.IsOutdated(policy.ConsentPolicyVersion))
            throw new ForbiddenException("CONSENT_REQUIRED");
        if (ParentStoryContext.IsEditorStory(story))
        {
            var manual = new ParentManualAuthoringPolicy(uow, guard);
            await manual.AuthorizeAsync(actor, story, ct);
            if (await manual.IsLockedAsync(story.Id, ct)) throw new ConflictException("PARENT_STORY_FINALIZED");
        }
        var rules = await uow.Repository<SafetyPolicyCategory>().FindAsync(x => x.SafetyPolicyId == policy.Id && !x.IsDeleted, cancellationToken: ct);
        var learning = await uow.Repository<LearningProfile>().FirstOrDefaultAsync(x => x.ChildProfileId == childId && !x.IsDeleted, cancellationToken: ct);
        return Hash(new { policy.MaxStoryLength, policy.ConsentRecordedAt, policy.ConsentPolicyVersion, PolicyUpdated = policy.UpdatedAt,
            child.AgeBand, ChildUpdated = child.UpdatedAt, ReadingLevel = learning?.ReadingLevel, LearningUpdated = learning?.UpdatedAt,
            Rules = rules.OrderBy(x => x.ContentCategoryId).Select(x => new { x.ContentCategoryId, x.Rule }).ToArray() });
    }
}
