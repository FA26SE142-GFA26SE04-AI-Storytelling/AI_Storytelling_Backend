using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.ChildProfiles.Safety;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.Interfaces;
using StoryPlatform.Application.Features.CreatorAssistance;
using StoryPlatform.Application.Features.CreatorAssistance.Services;
using StoryPlatform.Contracts.AI.CreatorAssistance;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ParentAuthoring;

public sealed record ParentAssistanceContext(string Fingerprint, AssistanceChildPolicy Policy);

public sealed class ParentAssistanceAccessPolicy(IUnitOfWork uow, IChildProfileAccessGuard guard) : IAssistanceAccessPolicy
{
    private readonly ParentManualAuthoringPolicy _manual = new(uow, guard);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public StoryScope Scope => StoryScope.Parent;
    public string RouteSegment => "parent";
    public async Task AuthorizeAsync(int user, Story story, CancellationToken ct)
    {
        if (story.Source != StorySource.Ai) { await _manual.AuthorizeAsync(user, story, ct); return; }
        if (story.Scope != StoryScope.Parent || story.AuthorUserId != user || story.IsDeleted || !story.ChildProfileId.HasValue)
            throw new StoryPlatform.Application.Common.Exceptions.NotFoundException("Story", story.Id);
        await guard.EnsureOwnerAsync(story.ChildProfileId.Value, user, ct);
    }
    public async Task EnsureWritableAsync(Story story, CancellationToken ct)
    {
        if (story.Source != StorySource.Ai) { await _manual.EnsureWritableAsync(story, ct); return; }
        await AssistanceGenerationAccess.EnsureWritableAsync(uow, story, ct);
    }

    public async Task<string?> CaptureContextAsync(Story story, CancellationToken ct)
    {
        var child = await uow.Repository<ChildProfile>().FirstOrDefaultAsync(x => x.Id == story.ChildProfileId, cancellationToken: ct);
        if (child is null || child.IsDeleted || child.Status != ChildProfileStatus.Active)
            throw new AssistanceException("CHILD_NOT_ACTIVE", 409);
        var policy = await uow.Repository<SafetyPolicy>().FirstOrDefaultAsync(x => x.ChildProfileId == child.Id && !x.IsDeleted, cancellationToken: ct);
        if (policy is null || !policy.ConsentRecorded || !policy.ConsentRecordedAt.HasValue || policy.ConsentPolicyVersion <= 0)
            throw new AssistanceException("CONSENT_REQUIRED", 403);
        if (ConsentPolicy.IsOutdated(policy.ConsentPolicyVersion)) throw new AssistanceException("CONSENT_OUTDATED", 403);
        var age = child.AgeBand == AgeBand.Age_9_10 ? "9-10" : "6-8";
        if (story.Language != child.Language || story.AgeBand != age)
            throw new AssistanceException("CHILD_CONTEXT_SETUP_REQUIRED", 409);
        if (policy.MaxStoryLength <= 0 || policy.ReadabilityScoreThreshold is < 0 or > 100 || policy.SafetyScoreThreshold is < 0 or > 100)
            throw new AssistanceException("CHILD_POLICY_INVALID", 409);
        var learning = await uow.Repository<LearningProfile>().FirstOrDefaultAsync(x => x.ChildProfileId == child.Id && !x.IsDeleted, cancellationToken: ct);
        if (learning?.ReadingLevel is < 1 or > 5) throw new AssistanceException("CHILD_POLICY_INVALID", 409);
        var assignments = await uow.Repository<SafetyPolicyCategory>().FindAsync(x => x.SafetyPolicyId == policy.Id && !x.IsDeleted, cancellationToken: ct);
        var categories = await uow.Repository<ContentCategory>().FindAsync(x => x.IsActive && !x.IsDeleted, cancellationToken: ct);
        string[] Terms(PolicyRule rule) => categories.Where(c => assignments.Any(a => a.ContentCategoryId == c.Id && a.Rule == rule))
            .OrderBy(c => c.Id).Select(c => c.Code + ": " + c.DisplayName).ToArray();
        var context = new AssistanceChildPolicy(policy.MaxStoryLength, learning?.ReadingLevel,
            Terms(PolicyRule.Blocked), Terms(PolicyRule.Restricted), policy.ReadabilityScoreThreshold, policy.SafetyScoreThreshold);
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            schema = 1, child.Id, child.AgeBand, child.Language, story.ExperienceMode, story.AudienceRevision,
            PolicyId = policy.Id, policy.ConsentRecordedAt, policy.ConsentPolicyVersion, policy.ConsentedByUserId,
            policy.RequiredApprovalMode, policy.ParentalGateEnabled, policy.AllowInteractiveStories, context
        }, Json))));
        return JsonSerializer.Serialize(new ParentAssistanceContext(fingerprint, context), Json);
    }

    public async Task ValidateContextAsync(Story story, string? snapshot, CancellationToken ct)
    {
        if (snapshot is null || snapshot != await CaptureContextAsync(story, ct))
            throw new AssistanceException("AUDIENCE_CONTEXT_STALE", 409);
    }
}
