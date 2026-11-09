using System.Text.Json;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.Moderation.DTOs;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Application.Features.Moderation.Services;

/// <summary>Read-only assessment validity. Never invokes EvaluateAsync or writes moderation state.</summary>
public sealed class CreatorSafetyCheckService(IUnitOfWork uow, IGrayZoneEvaluator? evaluator)
{
    public static GrayZoneScan CreateScan(string phase, Story story, string title, string content, string? lesson,
        IEnumerable<string> blockedTerms)
    {
        var parts = GrayZoneEvaluator.TextParts(title, content);
        if (phase == "phase3") parts = parts.Concat([new ModerationText("section", "lesson", lesson ?? "")]).ToArray();
        return new(phase, story.AgeBand, story.Language, parts,
            blockedTerms.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().Order().ToArray()) { ReadingLevel = story.ReadingLevel };
    }

    public async Task<GrayZoneScan> BuildScanAsync(Story story, StoryVersion version, CancellationToken ct)
    {
        var categories = await uow.Repository<ContentCategory>().FindAsync(x => x.IsActive && !x.IsDeleted, cancellationToken: ct);
        var age = story.AgeBand == "9-10" ? "Age_9_10" : "Age_6_8";
        var terms = categories.Where(x => x.DefaultBlockedAgeBands.Contains(age)).SelectMany(x => new[] { x.Code, x.DisplayName });
        // A saved story/revision is a complete-content scan, even before its first assessment.
        // Deriving phase from the old assessment could omit Lesson and incorrectly reuse a phase1 result.
        var phase = string.IsNullOrWhiteSpace(version.Content) ? "phase1" : "phase3";
        return CreateScan(phase, story, version.Title ?? "", version.Content ?? "", version.Lesson, terms);
    }

    public static GrayZoneAssessment? Read(StoryVersion version)
    {
        try { return GrayZoneWorkflowService.ReadAssessment(version); }
        catch (Exception e) when (e is JsonException or InvalidOperationException or NotSupportedException) { return null; }
    }

    public async Task<bool> IsCurrentAsync(Story story, StoryVersion version, GrayZoneScan scan, GrayZoneAssessment assessment, CancellationToken ct)
        => assessment.Fingerprint == GrayZoneEvaluator.Fingerprint(scan)
            && (assessment.AgeBand == null || assessment.AgeBand == story.AgeBand)
            && (assessment.Language == null || assessment.Language == story.Language)
            // Older assessments predate the field; the immutable version still pins its audience revision.
            && (assessment.AudienceRevision ?? version.AudienceRevision) == story.AudienceRevision
            && evaluator != null && await evaluator.CanReuseAsync(assessment, ct);

    public async Task<CreatorSafetyCheckDto> GetAsync(Story story, StoryVersion version, CancellationToken ct)
    {
        var assessment = Read(version);
        var required = evaluator?.IsConfigured == true;
        if (!required) return new(false, "Unavailable", assessment?.Zone.ToString());
        if (assessment == null) return new(true, "NotChecked", null);
        try
        {
            var scan = await BuildScanAsync(story, version, ct);
            return new(true, await IsCurrentAsync(story, version, scan, assessment, ct) ? "Current" : "Stale", assessment.Zone.ToString());
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return new(true, "Unavailable", assessment.Zone.ToString()); }
        catch (Exception e) when (e is InvalidOperationException or HttpRequestException or AppException)
        { return new(true, "Unavailable", assessment.Zone.ToString()); }
    }
}
