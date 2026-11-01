using System.Text.Json;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ContentGeneration.Services;

public sealed partial class ContentGenerationService
{
    private async Task<bool> HasPromotedContentAsync(int jobId, CancellationToken ct)
    {
        var job = await _unitOfWork.Repository<StoryGenerationJob>().GetByIdAsync(jobId, ct);
        if (job?.Stage != JobStage.ContentStable || job.StoryVersionId is not int id) return false;
        var version = await _unitOfWork.Repository<StoryVersion>().GetByIdAsync(id, ct);
        return version is { IsCurrent: true, OutlineApprovedAt: not null } &&
            version.StoryId == job.StoryId && !string.IsNullOrWhiteSpace(version.Content);
    }

    private async Task CompleteStableHandoffAsync(int jobId, string claimedToken, CancellationToken ct)
    {
        var job = await _unitOfWork.Repository<StoryGenerationJob>().GetByIdAsync(jobId, ct);
        if (job?.Stage != JobStage.ContentStable) return; // e.g. paused for moderation
        job = await RequireClaimedJobAsync(jobId, claimedToken, ct);
        if (!await HasPromotedContentAsync(jobId, ct)) throw new ConflictException("STALE_BASE_VERSION");
        var story = await _unitOfWork.Repository<Story>().GetByIdAsync(job.StoryId, ct)
            ?? throw new NotFoundException("Story", job.StoryId);
        await _artifactHandoff.QueueArtifactsAsync(job.StoryId, job.StoryVersionId!.Value,
            job.RequestedByUserId ?? story.AuthorUserId, job.GenerationRequestId, ct);
        await InStoryTransactionAsync(job.StoryId, async () =>
        {
            var current = await RequireClaimedJobAsync(jobId, claimedToken, ct);
            current.Status = GenerationJobStatus.Completed;
            current.ErrorCode = null;
            current.CompletedAt = DateTime.UtcNow;
            current.LeaseExpiresAt = null;
            _unitOfWork.Repository<StoryGenerationJob>().Update(current);
            RotateToken(current);
            return true;
        }, ct);
    }

    private Task<bool> RetryStableHandoffAsync(int userId, int storyId, string key, CancellationToken ct) =>
        InStoryTransactionAsync(storyId, async () =>
        {
            var story = await LoadAuthorizedStoryAsync(userId, storyId, ct);
            var receipt = await _unitOfWork.Repository<StoryAuthoringOperation>().FirstOrDefaultAsync(
                x => x.StoryId == storyId && x.OperationId == key, cancellationToken: ct);
            if (receipt?.Kind == "content-handoff-retry")
            {
                if (receipt.ActorUserId != userId || !await _unitOfWork.Repository<StoryVersion>().ExistsAsync(
                    x => x.StoryId == storyId && x.Id == receipt.StoryVersionId && x.IsCurrent, ct))
                    throw new ConflictException("HANDOFF_RETRY_KEY_CONFLICT");
                return true;
            }
            if (story.Source != StorySource.Ai || story.Status != StoryStatus.OutlineReview) return false;
            var version = await _unitOfWork.Repository<StoryVersion>().FirstOrDefaultAsync(
                x => x.StoryId == storyId && x.IsCurrent && x.Content != null && x.OutlineApprovedAt != null, cancellationToken: ct);
            if (version is null) return false;
            var job = (await _unitOfWork.Repository<StoryGenerationJob>().FindAsync(
                x => x.StoryId == storyId && x.StoryVersionId == version.Id && x.Operation == GenerationJobOperation.GenerateContent,
                cancellationToken: ct)).OrderByDescending(x => x.Id).FirstOrDefault();
            if (job is null || job.Status is not (GenerationJobStatus.Failed or GenerationJobStatus.Completed)) return false;
            if (job.Status == GenerationJobStatus.Completed && await _unitOfWork.Repository<StoryGenerationJob>().ExistsAsync(
                x => x.StoryId == storyId && x.StoryVersionId == version.Id && x.Operation == GenerationJobOperation.GenerateVocabulary, ct)) return false;
            CandidateMetadataEnvelope? metadata;
            try { metadata = JsonSerializer.Deserialize<CandidateMetadataEnvelope>(job.GenerationMetadataJson ?? "null", JsonOptions); }
            catch (JsonException) { return false; }
            if (metadata?.Quality?.IsPassed != true) return false;
            if (receipt is not null) throw new ConflictException("HANDOFF_RETRY_KEY_CONFLICT");
            job.Status = GenerationJobStatus.Pending;
            job.Stage = JobStage.ContentStable;
            job.AttemptNo = 0;
            job.ErrorCode = null;
            job.CompletedAt = job.LeaseExpiresAt = null;
            _unitOfWork.Repository<StoryGenerationJob>().Update(job);
            RotateToken(job);
            await _unitOfWork.Repository<StoryAuthoringOperation>().AddAsync(new()
            {
                StoryId = storyId, StoryVersionId = version.Id, ActorUserId = userId,
                OperationId = key, Kind = "content-handoff-retry", RequestHash = $"content-handoff:{version.Id}",
                OperationResponseJson = JsonSerializer.Serialize(new { job.Id }, JsonOptions)
            }, ct);
            return true;
        }, ct);
}
