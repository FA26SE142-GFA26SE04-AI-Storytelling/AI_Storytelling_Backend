using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.Safety;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.Moderation.Services;

public sealed record GrayZoneContentCheckDto(int StoryVersionId, string Status, bool WaitingForModeration);

public sealed class GrayZoneContentCheckService(IUnitOfWork uow, IGrayZoneEvaluator evaluator,
    GrayZoneWorkflowService workflow, IChildProfileAccessGuard supervision)
{
    public bool IsConfigured => evaluator.IsConfigured;

    public async Task<GrayZoneContentCheckDto> CheckAsync(int userId, int storyId, int versionId, CancellationToken ct)
    {
        var user = await uow.Repository<UserAccount>().FirstOrDefaultAsync(x => x.Id == userId, cancellationToken: ct);
        var story = await uow.Repository<Story>().FirstOrDefaultAsync(x => x.Id == storyId && !x.IsDeleted, cancellationToken: ct)
            ?? throw new NotFoundException("Story", storyId);
        if (user is null || user.IsDeleted || user.Status != AccountStatus.Active || user.LockedUntil > DateTime.UtcNow
            || story.Scope == StoryScope.Creator && (user.Role != UserRole.ContentCreator || story.AuthorUserId != userId))
            throw new ForbiddenException("AUTHORING_ACCESS_REQUIRED");
        var blocked = new List<string>();
        if (story.Scope == StoryScope.Parent)
        {
            if (!story.ChildProfileId.HasValue) throw new ConflictException("CHILD_CONTEXT_REQUIRED");
            await supervision.EnsureOwnerAsync(story.ChildProfileId.Value, userId, ct);
            if (await uow.Repository<ParentStoryOperation>().ExistsAsync(x => x.StoryId == storyId
                && x.Kind == "FamilyConfirmation" && !x.IsDeleted && x.RevokedAt == null, ct))
                throw new ConflictException("PARENT_STORY_FINALIZED");
            var policy = await uow.Repository<SafetyPolicy>().FirstOrDefaultAsync(x => x.ChildProfileId == story.ChildProfileId, cancellationToken: ct)
                ?? throw new ConflictException("SAFETY_POLICY_REQUIRED");
            if (!policy.ConsentRecorded || !policy.ConsentRecordedAt.HasValue || policy.ConsentPolicyVersion <= 0)
                throw new ForbiddenException("CONSENT_REQUIRED");
            if (ConsentPolicy.IsOutdated(policy.ConsentPolicyVersion)) throw new ForbiddenException("CONSENT_OUTDATED");
            var rules = await uow.Repository<SafetyPolicyCategory>().FindAsync(x => x.SafetyPolicyId == policy.Id && x.Rule == PolicyRule.Blocked, cancellationToken: ct);
            var ids = rules.Select(x => x.ContentCategoryId).ToArray();
            var categories = await uow.Repository<ContentCategory>().FindAsync(x => x.IsActive && ids.Contains(x.Id), cancellationToken: ct);
            blocked.AddRange(categories.SelectMany(x => new[] { x.Code, x.DisplayName }));
        }
        else
        {
            var categories = await uow.Repository<ContentCategory>().FindAsync(x => x.IsActive, cancellationToken: ct);
            var age = story.AgeBand == "9-10" ? "Age_9_10" : "Age_6_8";
            blocked.AddRange(categories.Where(x => x.DefaultBlockedAgeBands.Contains(age)).SelectMany(x => new[] { x.Code, x.DisplayName }));
        }
        var version = await uow.Repository<StoryVersion>().FirstOrDefaultAsync(x => x.Id == versionId && x.StoryId == storyId && x.IsCurrent, cancellationToken: ct)
            ?? throw new ConflictException("CURRENT_SAVED_VERSION_REQUIRED");
        if (!evaluator.IsConfigured) throw new ConflictException("MODERATION_NOT_CONFIGURED");
        var prior = GrayZoneWorkflowService.ReadAssessment(version);
        var phase = prior?.Phase == "phase3" ? "phase3" : "phase1";
        var parts = GrayZoneEvaluator.TextParts(version.Title ?? "", version.Content ?? "");
        if (phase == "phase3") parts = parts.Concat([new ModerationText("section", "lesson", version.Lesson ?? "")]).ToArray();
        var scan = new GrayZoneScan(phase, story.AgeBand, story.Language, parts, blocked) { ReadingLevel = story.ReadingLevel };
        var assessment = prior is not null && prior.Fingerprint == GrayZoneEvaluator.Fingerprint(scan) && await evaluator.CanReuseAsync(prior, ct)
            ? prior : await evaluator.EvaluateAsync(scan, ct);
        assessment = assessment with { AudienceRevision = story.AudienceRevision };
        var waiting = await workflow.RecordAsync(storyId, versionId, null, assessment, true, ct);
        return new(versionId, assessment.Zone.ToString(), waiting);
    }
}
