using System.Text.Json;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.Moderation.DTOs;
using StoryPlatform.Application.Features.Notifications.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.Moderation.Services;

public sealed record GrayZoneCheckpoint(int? JobId, string Fingerprint, bool QualityPassed);
public sealed record GrayZoneDecisionRequest(string Decision, string Reason, int RowVersion);
public sealed record GrayZoneReviewDto(int ItemId, int StoryVersionId, int VersionNo, string Title,
    string Content, string AgeBand, int? ReadingLevel, string? Flags, int RowVersion);

/// <summary>Uses existing version/item jsonb columns. Never starts generation or calls a provider under a lock.</summary>
public sealed class GrayZoneWorkflowService(IUnitOfWork uow, IAuditLogWriter audit, INotificationService? notifications = null)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static GrayZoneAssessment? ReadAssessment(StoryVersion version)
    {
        var root = PublicReviewState.ParseOrNew(version.Moderation.GrayZoneFlags);
        return root["assessment"]?.Deserialize<GrayZoneAssessment>(Json);
    }

    public async Task<bool> RecordAsync(int storyId, int versionId, int? jobId, GrayZoneAssessment assessment,
        bool qualityPassed, CancellationToken ct, string? jobToken = null, bool finalCandidate = true)
    {
        await uow.BeginTransactionAsync(ct);
        try
        {
            await uow.AcquireTransactionLockAsync(storyId, ct);
            var story = await uow.Repository<Story>().FirstOrDefaultAsync(x => x.Id == storyId, cancellationToken: ct)
                ?? throw new NotFoundException("Story", storyId);
            var version = await uow.Repository<StoryVersion>().FirstOrDefaultAsync(x => x.Id == versionId && x.StoryId == storyId, cancellationToken: ct)
                ?? throw new NotFoundException("StoryVersion", versionId);
            if (assessment.AgeBand is not null && assessment.AgeBand != story.AgeBand
                || assessment.Language is not null && assessment.Language != story.Language
                || assessment.AudienceRevision.HasValue && assessment.AudienceRevision != story.AudienceRevision)
                throw new ConflictException("MODERATION_CONTEXT_CHANGED");
            if (story.IsDeleted || story.Status == StoryStatus.Archived || story.IsPublished
                || version.Moderation.ReviewStatus != VersionReviewStatus.None)
                throw new ConflictException("MODERATION_SOURCE_LOCKED");
            if (story.Scope == StoryScope.Parent && !jobId.HasValue && await uow.Repository<ParentStoryOperation>().ExistsAsync(
                x => x.StoryId == storyId && x.Kind == "FamilyConfirmation" && !x.IsDeleted && x.RevokedAt == null, ct))
                throw new ConflictException("PARENT_STORY_FINALIZED");
            StoryGenerationJob? job = null;
            if (jobId.HasValue)
            {
                job = await uow.Repository<StoryGenerationJob>().FirstOrDefaultAsync(x => x.Id == jobId.Value, cancellationToken: ct);
                if (job is null || job.StoryId != storyId || job.StoryVersionId != versionId || job.Status != GenerationJobStatus.Processing
                    || jobToken is not null && job.ConcurrencyToken != jobToken
                    || !await uow.Repository<StoryVersion>().ExistsAsync(x => x.Id == job.BaseStoryVersionId && x.IsCurrent, ct))
                    throw new ConflictException("MODERATION_SOURCE_CHANGED");
                uow.Repository<StoryGenerationJob>().Update(job);
            }
            else if (!version.IsCurrent) throw new ConflictException("MODERATION_SOURCE_CHANGED");

            var prior = ReadAssessment(version);
            if (prior is not null && (prior.Fingerprint != assessment.Fingerprint || prior.PolicyVersion != assessment.PolicyVersion
                || prior.Zone != assessment.Zone || prior.Model != assessment.Model || prior.RiskScore != assessment.RiskScore)
                && await uow.Repository<ModerationItem>().ExistsAsync(x => x.StoryVersionId == versionId
                    && x.ItemType == ModerationItemType.GrayZoneStory
                    && (x.Status == ModerationItemStatus.Pending || x.Status == ModerationItemStatus.Claimed), ct))
                throw new ConflictException("MODERATION_REVIEW_IN_PROGRESS");
            var cleared = version.Moderation.GrayZoneCleared && prior?.Fingerprint == assessment.Fingerprint
                && prior.PolicyVersion == assessment.PolicyVersion && prior.Provider == assessment.Provider
                && prior.Model == assessment.Model && prior.RiskScore == assessment.RiskScore;
            version.Moderation.ModerationZone = assessment.Zone;
            if (!cleared)
            {
                version.Moderation.GrayZoneCleared = false;
                version.Moderation.GrayZoneClearedAt = null;
                version.Moderation.GrayZoneClearedByUserId = null;
            }
            version.Moderation.GrayZoneFlags = JsonSerializer.Serialize(new
            {
                assessment,
                flags = assessment.Zone == ModerationZone.GrayZone ? assessment.Findings.Select((f, i) => new
                {
                    id = $"{assessment.Fingerprint[..16]}:{i}",
                    kind = f.Kind,
                    name = f.Name,
                    reason = f.Reason,
                    category = f.Category,
                    evidenceQuote = f.EvidenceQuote,
                    contextNote = f.ContextNote,
                    startOffset = f.StartOffset,
                    endOffset = f.EndOffset
                }).ToArray() : []
            }, Json);
            uow.Repository<StoryVersion>().Update(version);
            var wait = assessment.Zone == ModerationZone.GrayZone && story.Scope == StoryScope.Parent && !cleared && finalCandidate;
            if (assessment.Zone == ModerationZone.GrayZone)
            {
                story.EverGrayZone = true;
                uow.Repository<Story>().Update(story);
                if (wait)
                {
                    var existing = await uow.Repository<ModerationItem>().FirstOrDefaultAsync(x =>
                        x.ItemType == ModerationItemType.GrayZoneStory && x.StoryVersionId == versionId
                        && (x.Status == ModerationItemStatus.Pending || x.Status == ModerationItemStatus.Claimed || x.Status == ModerationItemStatus.Blocked), cancellationToken: ct);
                    if (existing is not null && existing.Status == ModerationItemStatus.Blocked)
                        throw new ConflictException("GRAY_ZONE_BLOCKED");
                    if (existing is null)
                        await uow.Repository<ModerationItem>().AddAsync(new ModerationItem
                        {
                            ItemType = ModerationItemType.GrayZoneStory,
                            Status = ModerationItemStatus.Pending,
                            Priority = ModerationPriority.High,
                            StoryId = storyId,
                            StoryVersionId = versionId,
                            CreatedByUserId = story.AuthorUserId,
                            SlaDueAt = DateTime.UtcNow.AddHours(24),
                            Flags = JsonSerializer.Serialize(new GrayZoneCheckpoint(jobId, assessment.Fingerprint, qualityPassed), Json)
                        }, ct);
                    if (job is not null)
                    {
                        job.Status = GenerationJobStatus.WaitingForModeration;
                        job.LeaseExpiresAt = null;
                        job.ConcurrencyToken = Guid.NewGuid().ToString("N");
                        uow.Repository<StoryGenerationJob>().Update(job);
                    }
                }
            }
            await uow.CommitTransactionAsync(ct);
            return wait;
        }
        catch { await uow.RollbackTransactionAsync(CancellationToken.None); throw; }
    }

    private async Task<ModerationItem> RequireClaimAsync(int userId, int id, CancellationToken ct)
    {
        var user = await uow.Repository<UserAccount>().FirstOrDefaultAsync(x => x.Id == userId, cancellationToken: ct);
        if (user is null || user.IsDeleted || user.Status != AccountStatus.Active
            || user.Role is not (UserRole.Moderator or UserRole.Administrator)) throw new ForbiddenException("MODERATOR_REQUIRED");
        var item = await uow.Repository<ModerationItem>().FirstOrDefaultAsync(x => x.Id == id, cancellationToken: ct)
            ?? throw new NotFoundException("ModerationItem", id);
        if (item.ItemType != ModerationItemType.GrayZoneStory || item.Status != ModerationItemStatus.Claimed
            || item.ClaimedByUserId != userId || item.ClaimedAt is null
            || item.ClaimedAt.Value.Add(ModerationQueuePolicy.ClaimTimeout) <= DateTime.UtcNow
            || item.CreatedByUserId == userId) throw new ForbiddenException("VALID_MODERATION_CLAIM_REQUIRED");
        return item;
    }

    public async Task<GrayZoneReviewDto> GetAsync(int userId, int id, CancellationToken ct)
    {
        var item = await RequireClaimAsync(userId, id, ct);
        var version = await uow.Repository<StoryVersion>().FirstOrDefaultAsync(x => x.Id == item.StoryVersionId && x.StoryId == item.StoryId, cancellationToken: ct)
            ?? throw new NotFoundException("StoryVersion", item.StoryVersionId ?? 0);
        var story = await uow.Repository<Story>().FirstOrDefaultAsync(x => x.Id == item.StoryId, cancellationToken: ct)
            ?? throw new NotFoundException("Story", item.StoryId ?? 0);
        await audit.LogAsync(userId, "GrayZoneReviewOpened", nameof(ModerationItem), id, null, new { versionId = version.Id }, ct);
        // Do not expose the assessment score, checkpoint or child identity in the package.
        var flags = PublicReviewState.ParseVersionFlags(version.Moderation.GrayZoneFlags)
            .Select(x => new
            {
                x.Id,
                x.Kind,
                x.Name,
                CriterionCode = x.Category,
                Reason = GrayZoneEvaluator.Redact(x.Reason ?? ""),
                EvidenceQuote = GrayZoneEvaluator.Redact(x.EvidenceQuote ?? ""),
                ContextNote = GrayZoneEvaluator.Redact(x.ContextNote ?? ""),
                x.StartOffset,
                x.EndOffset
            });
        return new(id, version.Id, version.VersionNo, GrayZoneEvaluator.Redact(version.Title ?? ""),
            GrayZoneEvaluator.Redact(version.Content ?? ""), story.AgeBand, story.ReadingLevel,
            JsonSerializer.Serialize(flags, Json), item.RowVersion);
    }

    public async Task DecideAsync(int userId, int id, GrayZoneDecisionRequest request, CancellationToken ct)
    {
        if (request.Decision is not ("Clear" or "Block") || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 2000)
            throw new BadRequestException("GRAY_ZONE_DECISION_INVALID");
        var initial = await RequireClaimAsync(userId, id, ct);
        await uow.BeginTransactionAsync(ct);
        try
        {
            await uow.AcquireTransactionLockAsync(initial.StoryId!.Value, ct);
            var item = await RequireClaimAsync(userId, id, ct);
            // Attach the original concurrency value before incrementing a detached read.
            uow.Repository<ModerationItem>().Update(item);
            if (item.RowVersion != request.RowVersion) throw new ConflictException("STALE_MODERATION_ITEM");
            var checkpoint = TryReadCheckpoint(item.Flags)
                ?? throw new ConflictException("MODERATION_CHECKPOINT_MISSING");
            var story = await uow.Repository<Story>().FirstOrDefaultAsync(x => x.Id == item.StoryId, cancellationToken: ct);
            var version = await uow.Repository<StoryVersion>().FirstOrDefaultAsync(x => x.Id == item.StoryVersionId, cancellationToken: ct);
            if (story is null || story.IsDeleted || story.Scope != StoryScope.Parent || story.Status == StoryStatus.Archived
                || version is null || version.StoryId != story.Id || ReadAssessment(version)?.Fingerprint != checkpoint.Fingerprint)
                throw new ConflictException("MODERATION_SOURCE_CHANGED");
            StoryGenerationJob? job = null;
            if (checkpoint.JobId.HasValue)
            {
                job = await uow.Repository<StoryGenerationJob>().FirstOrDefaultAsync(x => x.Id == checkpoint.JobId, cancellationToken: ct);
                if (job is null || job.Status != GenerationJobStatus.WaitingForModeration || job.StoryVersionId != version.Id
                    || !await uow.Repository<StoryVersion>().ExistsAsync(x => x.Id == job.BaseStoryVersionId && x.IsCurrent, ct))
                    throw new ConflictException("MODERATION_SOURCE_CHANGED");
                uow.Repository<StoryGenerationJob>().Update(job);
            }
            else if (!version.IsCurrent) throw new ConflictException("MODERATION_SOURCE_CHANGED");
            var clear = request.Decision == "Clear";
            item.Status = clear ? ModerationItemStatus.Cleared : ModerationItemStatus.Blocked;
            item.DecisionReason = request.Reason.Trim(); item.DecidedByUserId = userId;
            item.DecidedAt = DateTime.UtcNow; item.RowVersion++;
            version.Moderation.GrayZoneCleared = clear; version.Moderation.GrayZoneClearedByUserId = clear ? userId : null;
            version.Moderation.GrayZoneClearedAt = clear ? item.DecidedAt : null;
            if (!clear && job is not null && !version.IsCurrent)
            {
                var oldCurrent = await uow.Repository<StoryVersion>().FirstOrDefaultAsync(x => x.StoryId == story.Id && x.IsCurrent, cancellationToken: ct)
                    ?? throw new ConflictException("MODERATION_SOURCE_CHANGED");
                oldCurrent.IsCurrent = false; uow.Repository<StoryVersion>().Update(oldCurrent);
                await uow.SaveChangesAsync(ct);
                version.IsCurrent = true;
                story.Status = StoryStatus.ContentReview; story.Title = version.Title;
                story.Content = version.Content; story.MoralLesson = version.Lesson;
                uow.Repository<Story>().Update(story);
            }
            if (job is not null)
            {
                job.Status = clear ? GenerationJobStatus.Pending : GenerationJobStatus.Failed;
                job.ErrorCode = clear ? null : "GRAY_ZONE_BLOCKED";
                job.ConcurrencyToken = Guid.NewGuid().ToString("N"); job.LeaseExpiresAt = null;
                if (clear) job.AttemptNo = 0;
                uow.Repository<StoryGenerationJob>().Update(job);
            }
            uow.Repository<ModerationItem>().Update(item); uow.Repository<StoryVersion>().Update(version);
            await audit.LogAsync(userId, "GrayZone" + request.Decision, nameof(ModerationItem), id, null,
                new { versionId = version.Id, reason = request.Reason.Trim() }, ct);
            if (notifications is not null)
                await notifications.CreateAsync(story.AuthorUserId, NotificationType.ContentModerationResult,
                    JsonSerializer.Serialize(new
                    {
                        itemId = id,
                        storyId = story.Id,
                        storyVersionId = version.Id,
                        decision = request.Decision,
                        reason = clear ? null : "SAFETY_REVIEW_BLOCKED",
                        categories = clear ? Array.Empty<string>() : ReadAssessment(version)!.Findings.Select(x => x.Category).Distinct().ToArray()
                    }, Json), ct);
            await uow.CommitTransactionAsync(ct);
        }
        catch { await uow.RollbackTransactionAsync(CancellationToken.None); throw; }
    }

    private static GrayZoneCheckpoint? TryReadCheckpoint(string? flags)
    {
        if (string.IsNullOrWhiteSpace(flags)) return null;
        try
        {
            var checkpoint = JsonSerializer.Deserialize<GrayZoneCheckpoint>(flags, Json);
            return checkpoint is not null && !string.IsNullOrWhiteSpace(checkpoint.Fingerprint) ? checkpoint : null;
        }
        catch (JsonException) { return null; }
    }

    public async Task CloseObsoleteAsync(CancellationToken ct)
    {
        var candidates = await uow.Repository<ModerationItem>().FindAsync(x => x.ItemType == ModerationItemType.GrayZoneStory
            && (x.Status == ModerationItemStatus.Pending || x.Status == ModerationItemStatus.Claimed), cancellationToken: ct);
        foreach (var candidate in candidates)
        {
            if (!candidate.StoryId.HasValue) continue;
            await uow.BeginTransactionAsync(ct);
            try
            {
                await uow.AcquireTransactionLockAsync(candidate.StoryId.Value, ct);
                var item = await uow.Repository<ModerationItem>().FirstOrDefaultAsync(x => x.Id == candidate.Id, cancellationToken: ct);
                if (item is null || item.Status is not (ModerationItemStatus.Pending or ModerationItemStatus.Claimed))
                { await uow.CommitTransactionAsync(ct); continue; }
                var story = await uow.Repository<Story>().FirstOrDefaultAsync(x => x.Id == item.StoryId, cancellationToken: ct);
                var checkpoint = TryReadCheckpoint(item.Flags);
                // Legacy queue items are valid but have no new continuation checkpoint.
                // Leave them to their existing claim/SLA handling instead of aborting the sweep.
                if (checkpoint is null) { await uow.CommitTransactionAsync(ct); continue; }
                var job = checkpoint.JobId.HasValue
                    ? await uow.Repository<StoryGenerationJob>().FirstOrDefaultAsync(x => x.Id == checkpoint.JobId, cancellationToken: ct) : null;
                var obsolete = story is null || story.IsDeleted || story.Status == StoryStatus.Archived
                    || (checkpoint.JobId.HasValue ? job is null || job.Status == GenerationJobStatus.Cancelled
                        || !await uow.Repository<StoryVersion>().ExistsAsync(x => x.Id == job.BaseStoryVersionId && x.IsCurrent, ct)
                        : !await uow.Repository<StoryVersion>().ExistsAsync(x => x.Id == item.StoryVersionId && x.IsCurrent, ct));
                if (obsolete)
                {
                    uow.Repository<ModerationItem>().Update(item);
                    if (job is not null) uow.Repository<StoryGenerationJob>().Update(job);
                    item.Status = ModerationItemStatus.Closed; item.ClosedReason = "SOURCE_CANCELLED_OR_REPLACED";
                    item.RowVersion++; uow.Repository<ModerationItem>().Update(item);
                    if (job?.Status == GenerationJobStatus.WaitingForModeration)
                    {
                        job.Status = GenerationJobStatus.Cancelled; job.ConcurrencyToken = Guid.NewGuid().ToString("N");
                        uow.Repository<StoryGenerationJob>().Update(job);
                    }
                }
                await uow.CommitTransactionAsync(ct);
            }
            catch { await uow.RollbackTransactionAsync(CancellationToken.None); throw; }
        }
    }
}
