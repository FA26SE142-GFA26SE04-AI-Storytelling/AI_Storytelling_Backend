using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AIStoryInput.Models;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.Safety;
using StoryPlatform.Application.Features.ContentGeneration;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ParentAuthoring;

public sealed record ParentPackageAudience(AIStoryInputContextSnapshot Profile, bool ConsentValid, string Fingerprint, bool AllowInteractiveStories = true);

public static class ParentPackageContext
{
    public static string ErrorCode(Exception error) => error is StoryPlatform.Application.Abstractions.AI.AIServiceRequestException ai ? ai.ErrorCode :
        System.Text.RegularExpressions.Regex.IsMatch(error.Message, "^[A-Z][A-Z0-9_]{2,99}$") ? error.Message : "PARENT_PACKAGE_GENERATION_FAILED";
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static string Hash(object value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, Json))));
    public static string Source(Story story, StoryVersion version) => Hash(new { version.Id, version.StoryId, version.Title,
        version.Content, version.Lesson, story.Language });
    public static async Task<ParentPackageAudience> CaptureAsync(IUnitOfWork uow, IChildProfileAccessGuard guard,
        int actor, int childId, CancellationToken ct, StoryPlatform.Contracts.AI.Models.LearningGenerationProfile? generationProfile = null)
    {
        var user = await uow.Repository<UserAccount>().FirstOrDefaultAsync(x => x.Id == actor && !x.IsDeleted, cancellationToken: ct);
        if (user?.Role != UserRole.User || user.Status != AccountStatus.Active || user.LockedUntil > DateTime.UtcNow)
            throw new ForbiddenException();
        await guard.EnsureOwnerAsync(childId, actor, ct);
        var child = await uow.Repository<ChildProfile>().FirstOrDefaultAsync(x => x.Id == childId && !x.IsDeleted, cancellationToken: ct);
        if (child?.Status != ChildProfileStatus.Active) throw new ConflictException("CHILD_NOT_ACTIVE");
        var policy = await uow.Repository<SafetyPolicy>().FirstOrDefaultAsync(x => x.ChildProfileId == childId && !x.IsDeleted, cancellationToken: ct)
            ?? throw new ConflictException("SAFETY_POLICY_REQUIRED");
        var learning = await uow.Repository<LearningProfile>().FirstOrDefaultAsync(x => x.ChildProfileId == childId && !x.IsDeleted, cancellationToken: ct);
        if (policy.MaxStoryLength <= 0 || policy.ReadabilityScoreThreshold is < 0 or > 100 || policy.SafetyScoreThreshold is < 0 or > 100 ||
            learning?.ReadingLevel is < 1 or > 5) throw new ConflictException("CHILD_POLICY_INVALID");
        var rules = await uow.Repository<SafetyPolicyCategory>().FindAsync(x => x.SafetyPolicyId == policy.Id && !x.IsDeleted, cancellationToken: ct);
        var categories = await uow.Repository<ContentCategory>().FindAsync(x => x.IsActive && !x.IsDeleted, cancellationToken: ct);
        string[] Terms(PolicyRule rule) => categories.Where(c => rules.Any(r => r.ContentCategoryId == c.Id && r.Rule == rule))
            .OrderBy(c => c.Id).SelectMany(c => new[] { c.Code, c.DisplayName }).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToArray();
        var profile = new AIStoryInputContextSnapshot(child.Id, child.AgeBand == AgeBand.Age_9_10 ? "9-10" : "6-8",
            learning?.ReadingLevel ?? 2, $"level_{learning?.ReadingLevel ?? 2}", child.Language, policy.MaxStoryLength, policy.RequiredApprovalMode.ToString(), [], [], [], [],
            RestrictedCategoryTerms: Terms(PolicyRule.Restricted), BlockedCategoryTerms: Terms(PolicyRule.Blocked),
            ReadabilityScoreThreshold: policy.ReadabilityScoreThreshold, SafetyScoreThreshold: policy.SafetyScoreThreshold,
            ParentalGateEnabled: policy.ParentalGateEnabled, ComprehensionGoal: learning?.ComprehensionGoal,
            ConsentPolicyVersion: policy.ConsentPolicyVersion, ConsentRecordedAt: policy.ConsentRecordedAt)
            { LearningProfile = generationProfile ?? new LearningBatchOptions().Profile(child.AgeBand == AgeBand.Age_9_10 ? "9-10" : "6-8") };
        var consent = policy.ConsentRecorded && policy.ConsentRecordedAt.HasValue && !ConsentPolicy.IsOutdated(policy.ConsentPolicyVersion);
        return new(profile, consent, Hash(new { profile, consent, child.OwnerUserId, child.Status, policy.AllowInteractiveStories,
            policy.ParentalGateEnabled, policy.ConsentedByUserId, rules = rules.OrderBy(x => x.ContentCategoryId).Select(x => new { x.ContentCategoryId, x.Rule }) }), policy.AllowInteractiveStories);
    }

    public static async Task<(Story Story, StoryVersion Version, ParentPackageAudience Audience)> ValidateAsync(
        IUnitOfWork uow, IChildProfileAccessGuard guard, ParentStoryPackage package, CancellationToken ct, bool requireConsent = false)
    {
        var story = await uow.Repository<Story>().FirstOrDefaultAsync(x => x.Id == package.StoryId && !x.IsDeleted, cancellationToken: ct);
        var version = await uow.Repository<StoryVersion>().FirstOrDefaultAsync(x => x.Id == package.StoryVersionId && !x.IsDeleted, cancellationToken: ct);
        if (story is null || !StoryPlatform.Application.Common.ParentStoryContext.IsEditorStory(story) || story.AuthorUserId != package.CreatedByUserId ||
            version is null || version.StoryId != story.Id || Source(story, version) != package.SourceFingerprint)
            throw new ConflictException("PARENT_PACKAGE_SOURCE_CHANGED");
        var pinned = JsonSerializer.Deserialize<ParentPackageAudience>(package.ContextSnapshotJson, Json) ?? throw new ConflictException("PACKAGE_CONTEXT_INVALID");
        var audience = await CaptureAsync(uow, guard, package.CreatedByUserId, package.ChildProfileId, ct, pinned.Profile.LearningProfile);
        if (audience.Fingerprint != package.ContextFingerprint) throw new ConflictException("CHILD_CONTEXT_CHANGED");
        if (requireConsent && !audience.ConsentValid) throw new ForbiddenException("CONSENT_REQUIRED");
        return (story, version, audience);
    }
}
