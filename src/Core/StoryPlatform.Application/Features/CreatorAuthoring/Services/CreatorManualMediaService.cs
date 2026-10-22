using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.MediaGeneration.Interfaces;
using StoryPlatform.Application.Features.MediaGeneration.Models;
using StoryPlatform.Application.Features.MediaGeneration.Services;
using StoryPlatform.Application.Features.Moderation;
using StoryPlatform.Application.Features.Moderation.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.CreatorAuthoring.Services;

public sealed record ConfirmManualContentRequest(
    [Range(1, int.MaxValue)] int VersionId,
    [Required] string RevisionToken);

public sealed record ConfirmManualContentResult(int StoryId, int VersionId, int JobId, string Status,
    string JobStatus, string ProgressUrl, string PackageUrl);

/// <summary>Enqueues the existing Phase 5 worker; never generates or rewrites story content.</summary>
public sealed class CreatorManualMediaService(IUnitOfWork uow, GrayZoneContentCheckService checks,
    IGrayZoneEvaluator evaluator, IMediaContextBuilder contexts, IAuditLogWriter audit, CreatorAuthoringOptions options)
{
    public async Task<ConfirmManualContentResult> ConfirmAsync(int userId, int storyId, ConfirmManualContentRequest input, CancellationToken ct = default)
    {
        var (story, version) = await LoadAsync(userId, storyId, input, ct);
        await CreatorPublicationGuard.EnsureWritableAsync(uow, storyId, version.Id, ct);
        var prior = await JobAsync(storyId, version.Id, ct);
        if (prior is not null) return Result(story, prior);
        Validate(story, version);
        var source = Source(story, version);
        if (!checks.IsConfigured) throw new ConflictException("MODERATION_NOT_CONFIGURED");

        // Provider work stays outside the transaction; its failure leaves the draft editable.
        await checks.CheckAsync(userId, storyId, version.Id, ct);
        var checkedVersion = await uow.Repository<StoryVersion>().FirstOrDefaultAsync(x => x.Id == input.VersionId, cancellationToken: ct)
            ?? throw new ConflictException("STALE_STORY_VERSION");
        var checkedAssessment = GrayZoneWorkflowService.ReadAssessment(checkedVersion)
            ?? throw new ConflictException("CONTENT_MODERATION_CHECK_REQUIRED");

        await uow.BeginTransactionAsync(ct);
        try
        {
            await uow.AcquireTransactionLockAsync(storyId, ct);
            (story, version) = await LoadAsync(userId, storyId, input, ct);
            await CreatorPublicationGuard.EnsureWritableAsync(uow, storyId, version.Id, ct);
            prior = await JobAsync(storyId, version.Id, ct);
            if (prior is not null)
            {
                await uow.CommitTransactionAsync(ct);
                return Result(story, prior);
            }
            Validate(story, version);
            var assessment = GrayZoneWorkflowService.ReadAssessment(version);
            if (Source(story, version) != source || assessment is null || assessment.Fingerprint != checkedAssessment.Fingerprint ||
                assessment.PolicyVersion != checkedAssessment.PolicyVersion || !await evaluator.CanReuseAsync(assessment, ct))
                throw new ConflictException("CONTENT_MODERATION_CHECK_REQUIRED");
            GrayZoneGate.EnsureCanContinue(story, version);
            if (await uow.Repository<StoryGenerationJob>().ExistsAsync(x => x.StoryId == storyId &&
                (x.Status == GenerationJobStatus.Pending || x.Status == GenerationJobStatus.Processing), ct))
                throw new ConflictException("MEDIA_JOB_ACTIVE");

            // Pin manual audience/metadata without inventing an outline or a generation request.
            var contextJson = contexts.Build(new MediaContextBuildRequest(version.Id, version.Title, version.Content!, version.Lesson,
                null, null, null, null, JsonSerializer.Serialize(new
                {
                    audienceScope = "Creator",
                    creatorUserId = userId,
                    story.AgeBand,
                    story.Language,
                    story.ReadingLevel,
                    story.Genre,
                    story.MoralLesson,
                    story.AudienceRevision
                })));
            await uow.Repository<MediaContext>().AddAsync(new MediaContext { StoryVersionId = version.Id, Revision = 1, ContextJson = contextJson }, ct);
            MediaPackageConfirmation.Invalidate(version);
            uow.Repository<StoryVersion>().Update(version);
            var job = new StoryGenerationJob
            {
                StoryId = storyId,
                StoryVersionId = version.Id,
                BaseStoryVersionId = version.Id,
                RequestedByUserId = userId,
                OperationKey = $"p5:{storyId}:{version.Id}",
                Operation = GenerationJobOperation.GenerateMediaPackage,
                Stage = JobStage.MediaPending,
                Status = GenerationJobStatus.Pending,
                MaxAttempts = 3,
                StartedAt = DateTime.UtcNow
            };
            await uow.Repository<StoryGenerationJob>().AddAsync(job, ct);
            story.Status = StoryStatus.MediaProcessing;
            story.UpdatedAt = DateTime.UtcNow;
            uow.Repository<Story>().Update(story);
            await uow.SaveChangesAsync(ct);
            await audit.LogAsync(userId, "CreatorManualContentConfirmed", nameof(Story), storyId, null,
                new { storyVersionId = version.Id, jobId = job.Id }, ct);
            await uow.CommitTransactionAsync(ct);
            return Result(story, job);
        }
        catch { await uow.RollbackTransactionAsync(CancellationToken.None); throw; }
    }

    private async Task<(Story, StoryVersion)> LoadAsync(int userId, int storyId, ConfirmManualContentRequest input, CancellationToken ct)
    {
        var story = await uow.Repository<Story>().FirstOrDefaultAsync(x => x.Id == storyId && !x.IsDeleted, cancellationToken: ct)
            ?? throw new NotFoundException("Story", storyId);
        if (story.Scope != StoryScope.Creator || story.Source != StorySource.Manual || story.ChildProfileId is not null)
            throw new NotFoundException("Story", storyId);
        await MediaStoryAccess.AuthorizeCreatorAsync(uow, story, userId, ct);
        var version = await uow.Repository<StoryVersion>().FirstOrDefaultAsync(x => x.StoryId == storyId && x.IsCurrent && !x.IsDeleted, cancellationToken: ct);
        if (version is null || input.VersionId != version.Id || string.IsNullOrWhiteSpace(input.RevisionToken) || input.RevisionToken != story.AuthoringRevisionToken)
            throw new ConflictException("STALE_STORY_VERSION");
        return (story, version);
    }

    private void Validate(Story story, StoryVersion version)
    {
        if (story.Status != StoryStatus.Draft) throw new ConflictException("INVALID_STORY_STATUS");
        if (story.OutputMode != StoryOutputMode.ReadingMediaOnly) throw new ConflictException("OUTPUT_MODE_CONFLICT");
        if (string.IsNullOrWhiteSpace(version.Title) || string.IsNullOrWhiteSpace(version.Content) ||
            string.IsNullOrWhiteSpace(version.AuthoringMetadata.SourceDeclaration) || string.IsNullOrWhiteSpace(version.AuthoringMetadata.RightsDeclaration))
            throw new BadRequestException("MANUAL_CONTENT_INCOMPLETE");
        if (version.Title.Length > 200 || version.Content.Length > options.MaximumContentCharacters ||
            !options.SupportedLanguages.Contains(story.Language) || !options.SupportedAgeBands.Contains(story.AgeBand))
            throw new BadRequestException("MANUAL_CONTENT_INVALID");
    }

    private Task<StoryGenerationJob?> JobAsync(int storyId, int versionId, CancellationToken ct) =>
        uow.Repository<StoryGenerationJob>().FirstOrDefaultAsync(x => x.StoryId == storyId && x.StoryVersionId == versionId &&
            x.Operation == GenerationJobOperation.GenerateMediaPackage, cancellationToken: ct);

    private static string Source(Story s, StoryVersion v) => JsonSerializer.Serialize(new
    {
        v.Id,
        v.Title,
        v.Content,
        v.Lesson,
        s.AgeBand,
        s.Language,
        s.ReadingLevel,
        s.Genre,
        s.MoralLesson,
        s.AudienceRevision
    });

    private static ConfirmManualContentResult Result(Story s, StoryGenerationJob j) => new(s.Id, j.StoryVersionId!.Value, j.Id,
        s.Status.ToString(), j.Status.ToString(), $"/api/v1/stories/{s.Id}/media/progress", $"/api/v1/stories/{s.Id}/media/package");
}
