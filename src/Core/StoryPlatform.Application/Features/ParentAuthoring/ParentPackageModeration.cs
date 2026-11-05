using System.Text.Json;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.Services;
using StoryPlatform.Application.Features.Moderation;
using StoryPlatform.Application.Features.Moderation.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ParentAuthoring;

/// <summary>Package-specific gray-zone decisions use the existing queue/claim and do not clear unrelated profiles.</summary>
public static class ParentPackageModeration
{
    public static async Task<bool> IsObsoleteAsync(IUnitOfWork uow, ModerationItem item, CancellationToken ct)
    {
        try
        {
            var p = await Package(uow, item, ct);
            await ParentPackageContext.ValidateAsync(uow, new ChildProfileAccessGuard(uow), p, ct);
            return false;
        }
        catch (AppException) { return true; }
    }
    public static async Task<GrayZoneReviewDto> ReviewAsync(IUnitOfWork uow, ModerationItem item, CancellationToken ct)
    {
        var p = await Package(uow, item, ct);
        var (_, v, context) = await ParentPackageContext.ValidateAsync(uow, new ChildProfileAccessGuard(uow), p, ct);
        var assessment = JsonSerializer.Deserialize<GrayZoneAssessment>(p.GrayZoneAssessmentJson, ParentPackageContext.Json)!;
        return new(item.Id, v.Id, v.VersionNo, GrayZoneEvaluator.Redact(v.Title), GrayZoneEvaluator.Redact(v.Content!),
            context.Profile.AgeBand, context.Profile.ReadingLevel,
            JsonSerializer.Serialize(assessment.Findings.Select(x => new { x.Kind, x.Name, x.Category,
                Reason = GrayZoneEvaluator.Redact(x.Reason), EvidenceQuote = GrayZoneEvaluator.Redact(x.EvidenceQuote ?? ""), x.StartOffset, x.EndOffset }), ParentPackageContext.Json), item.RowVersion);
    }
    public static async Task DecideAsync(IUnitOfWork uow, ModerationItem item, bool clear, int moderator, string reason, CancellationToken ct)
    {
        var p = await Package(uow, item, ct);
        await ParentPackageContext.ValidateAsync(uow, new ChildProfileAccessGuard(uow), p, ct);
        var assessment = JsonSerializer.Deserialize<GrayZoneAssessment>(p.GrayZoneAssessmentJson, ParentPackageContext.Json)!;
        var queued = JsonSerializer.Deserialize<GrayZoneAssessment>(item.Flags ?? "{}", ParentPackageContext.Json);
        if (assessment.Zone != ModerationZone.GrayZone || queued?.Fingerprint != assessment.Fingerprint || queued.RubricHash != assessment.RubricHash || queued.PolicyVersion != assessment.PolicyVersion) throw new ConflictException("MODERATION_SOURCE_CHANGED");
        var check = ParentStoryPackageService.Check(p) ?? throw new ConflictException("PACKAGE_CHECK_NOT_PASSED");
        var remaining = clear ? check.Errors.Where(x => x != "GRAY_ZONE_REVIEW_REQUIRED").ToArray() : check.Errors.Append("GRAY_ZONE_BLOCKED").Distinct().ToArray();
        p.CheckResultJson = JsonSerializer.Serialize(check with { Passed = remaining.Length == 0, Errors = remaining }, ParentPackageContext.Json);
        p.Status = remaining.Length == 0 ? "Review" : "Blocked";
        ParentStoryPackageService.Rotate(uow, p); uow.Repository<ParentStoryPackage>().Update(p);
        item.Status = clear ? ModerationItemStatus.Cleared : ModerationItemStatus.Blocked;
        item.DecisionReason = reason; item.DecidedByUserId = moderator; item.DecidedAt = DateTime.UtcNow; item.RowVersion++;
        uow.Repository<ModerationItem>().Update(item);
    }
    private static async Task<ParentStoryPackage> Package(IUnitOfWork uow, ModerationItem item, CancellationToken ct) =>
        await uow.Repository<ParentStoryPackage>().FirstOrDefaultAsync(x => x.Id == item.ParentStoryPackageId && x.StoryId == item.StoryId &&
            x.StoryVersionId == item.StoryVersionId && x.Status == "Blocked" && !x.IsDeleted, cancellationToken: ct)
        ?? throw new ConflictException("MODERATION_SOURCE_CHANGED");
}
