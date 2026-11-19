using StoryPlatform.Application.Abstractions.AI;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.MediaGeneration;
using StoryPlatform.Application.Features.MediaGeneration.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.AiActivity.Services;

public sealed class AccountAiActivityService(IUnitOfWork unitOfWork, TimeProvider? clock = null) : IAccountAiActivityService
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public async Task<AiGenerationCapacityDto> GetCapacityAsync(int accountUserId, CancellationToken cancellationToken = default)
    {
        var activeStories = await unitOfWork.Repository<Story>().FindAsync(
            s => s.AuthorUserId == accountUserId && !s.IsDeleted && s.AiActivityMode != AiActivityMode.None,
            cancellationToken: cancellationToken);

        var items = activeStories
            .OrderBy(s => s.AiActivityStartedAt ?? s.CreatedAt)
            .ThenBy(s => s.Id)
            .Select(s => new ActiveAiStoryDto(
                s.Id,
                string.IsNullOrWhiteSpace(s.Title) ? $"Truyện #{s.Id}" : s.Title,
                s.AiActivityMode.ToString(),
                s.Status.ToString(),
                s.AiActivityStartedAt))
            .ToArray();

        return new AiGenerationCapacityDto(
            AiStoryConcurrencyLimits.MaxConcurrentStories,
            items.Length,
            items.Length < AiStoryConcurrencyLimits.MaxConcurrentStories,
            items);
    }

    public async Task EnsureCanAdmitNewStoryAsync(int accountUserId, CancellationToken cancellationToken = default)
    {
        var activeCount = await unitOfWork.Repository<Story>().CountAsync(
            s => s.AuthorUserId == accountUserId && !s.IsDeleted && s.AiActivityMode != AiActivityMode.None,
            cancellationToken);

        if (activeCount >= AiStoryConcurrencyLimits.MaxConcurrentStories)
        {
            throw new ConflictException(AiStoryConcurrencyLimits.ErrorMessage, AiStoryConcurrencyLimits.ErrorCode);
        }
    }

    public async Task AdmitStoryAsync(int accountUserId, int storyId, AiActivityMode requestedMode, CancellationToken cancellationToken = default)
    {
        if (requestedMode == AiActivityMode.None)
        {
            return;
        }

        var story = await unitOfWork.Repository<Story>().FirstOrDefaultAsync(
            s => s.Id == storyId && !s.IsDeleted,
            cancellationToken: cancellationToken);
        if (story is null || story.AuthorUserId != accountUserId)
        {
            return;
        }

        var now = _clock.GetUtcNow().UtcDateTime;

        if (story.AiActivityMode == AiActivityMode.FullCreation)
        {
            if (!story.AiActivityStartedAt.HasValue)
            {
                story.AiActivityStartedAt = now;
                unitOfWork.Repository<Story>().Update(story);
            }
            return;
        }

        if (story.AiActivityMode == AiActivityMode.TaskOnly)
        {
            if (requestedMode == AiActivityMode.FullCreation)
            {
                story.AiActivityMode = AiActivityMode.FullCreation;
                story.AiActivityStartedAt ??= now;
                unitOfWork.Repository<Story>().Update(story);
            }
            return;
        }

        await EnsureCanAdmitNewStoryAsync(accountUserId, cancellationToken);

        story.AiActivityMode = requestedMode;
        story.AiActivityStartedAt = now;
        unitOfWork.Repository<Story>().Update(story);
    }

    public async Task AdmitStoryAsync(int accountUserId, Story story, AiActivityMode requestedMode, CancellationToken cancellationToken = default)
    {
        await AdmitStoryAsync(accountUserId, story.Id, requestedMode, cancellationToken);
        // Admission may attach a different instance because queries use AsNoTracking.
        // Copy the authoritative fields back before the caller updates its Story.
        var admitted = await unitOfWork.Repository<Story>().GetByIdAsync(story.Id, cancellationToken);
        if (admitted is not null && admitted.AuthorUserId == accountUserId)
        {
            story.AiActivityMode = admitted.AiActivityMode;
            story.AiActivityStartedAt = admitted.AiActivityStartedAt;
        }
    }

    public async Task ReconcileStoryAiActivityAsync(int accountUserId, int storyId, CancellationToken cancellationToken = default, int? failedJobId = null)
    {
        var story = await unitOfWork.Repository<Story>().FirstOrDefaultAsync(
            s => s.Id == storyId,
            cancellationToken: cancellationToken);
        if (story is null || story.AuthorUserId != accountUserId || story.AiActivityMode == AiActivityMode.None)
        {
            return;
        }

        if (await HasActiveDurableOrInlineWorkAsync(storyId, cancellationToken))
        {
            return;
        }

        if (story.IsDeleted || story.ArchivedAt.HasValue)
        {
            Release(story);
            return;
        }

        if (story.AiActivityMode == AiActivityMode.TaskOnly)
        {
            Release(story);
            return;
        }

        if (failedJobId.HasValue && await IsCurrentTerminalFailureAsync(story, failedJobId.Value, cancellationToken))
        {
            Release(story);
            return;
        }

        // FullCreation lifecycle rules
        if (ParentStoryContext.IsEditorStory(story))
        {
            var currentVersion = await unitOfWork.Repository<StoryVersion>().FirstOrDefaultAsync(
                v => v.StoryId == storyId && v.IsCurrent && !v.IsDeleted,
                cancellationToken: cancellationToken);
            var packages = await unitOfWork.Repository<ParentStoryPackage>().FindAsync(
                p => p.StoryId == storyId && !p.IsDeleted &&
                     (currentVersion == null || p.StoryVersionId == currentVersion.Id) &&
                     p.Status != "Stale",
                cancellationToken: cancellationToken);

            if (packages.Count > 0)
            {
                if (packages.Any(p => p.Status is "PendingCheck" or "Checking" or "Review" or "Generating"))
                {
                    return;
                }

                Release(story);
                return;
            }
        }

        if (story.Status is StoryStatus.OutlineReview or StoryStatus.ContentReview or StoryStatus.MediaReview or StoryStatus.Approved)
        {
            return;
        }

        if (story.Status == StoryStatus.Ready)
        {
            var currentVersion = await unitOfWork.Repository<StoryVersion>().FirstOrDefaultAsync(
                v => v.StoryId == storyId && v.IsCurrent && !v.IsDeleted,
                cancellationToken: cancellationToken);
            if (currentVersion is not null &&
                currentVersion.MediaState.MediaPackageRevision > 0 &&
                !MediaPackageConfirmation.IsConfirmed(currentVersion))
            {
                return;
            }

            Release(story);
            return;
        }

        // MediaProcessing with no active jobs, Rejected, or Draft with no active requests/jobs/tasks
        Release(story);
    }

    private void Release(Story story)
    {
        story.AiActivityMode = AiActivityMode.None;
        story.AiActivityStartedAt = null;
        unitOfWork.Repository<Story>().Update(story);
    }

    private async Task<bool> IsCurrentTerminalFailureAsync(Story story, int jobId, CancellationToken ct)
    {
        var jobs = unitOfWork.Repository<StoryGenerationJob>();
        var failed = await jobs.FirstOrDefaultAsync(j => j.Id == jobId && j.StoryId == story.Id &&
            !j.IsDeleted && j.Status == GenerationJobStatus.Failed &&
            j.Operation != GenerationJobOperation.GrayZoneCheck, cancellationToken: ct);
        if (failed is null) return false;
        // A stale failure cannot end a newer attempt or a newer successful review.
        if (await jobs.ExistsAsync(j => j.StoryId == story.Id && !j.IsDeleted &&
            j.Operation != GenerationJobOperation.GrayZoneCheck &&
            (j.StartedAt > failed.StartedAt || j.StartedAt == failed.StartedAt && j.Id > failed.Id), ct)) return false;
        var current = await unitOfWork.Repository<StoryVersion>().FirstOrDefaultAsync(
            v => v.StoryId == story.Id && v.IsCurrent && !v.IsDeleted, cancellationToken: ct);
        return current is null || failed.StoryVersionId == current.Id || failed.BaseStoryVersionId == current.Id;
    }

    private async Task<bool> HasActiveDurableOrInlineWorkAsync(int storyId, CancellationToken cancellationToken)
    {
        if (await unitOfWork.Repository<StoryGenerationRequest>().ExistsAsync(
                r => r.StoryId == storyId && !r.IsDeleted &&
                     (r.Status == GenerationInputStatus.PendingInput ||
                      r.Status == GenerationInputStatus.CheckingInput),
                cancellationToken))
        {
            return true;
        }

        if (await unitOfWork.Repository<StoryGenerationJob>().ExistsAsync(
                j => j.StoryId == storyId && !j.IsDeleted &&
                     (j.Status == GenerationJobStatus.Pending ||
                      j.Status == GenerationJobStatus.Processing ||
                      j.Status == GenerationJobStatus.WaitingForModeration),
                cancellationToken))
        {
            return true;
        }

        if (await unitOfWork.Repository<StoryAssistanceTask>().ExistsAsync(
                t => t.StoryId == storyId && !t.IsDeleted && t.ActiveOperationId != null,
                cancellationToken))
        {
            return true;
        }

        if (await unitOfWork.Repository<LearningArtifactGenerationBatch>().ExistsAsync(
                b => b.StoryId == storyId && !b.IsDeleted &&
                     (b.Status == GenerationJobStatus.Pending ||
                      b.Status == GenerationJobStatus.Processing),
                cancellationToken))
        {
            return true;
        }

        return false;
    }
}
