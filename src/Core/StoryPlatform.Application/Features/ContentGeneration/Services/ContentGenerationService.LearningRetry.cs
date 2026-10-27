using System.Text.Json;
using StoryPlatform.Application.Common;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.Contracts.AI.Models;

namespace StoryPlatform.Application.Features.ContentGeneration.Services;

public sealed partial class ContentGenerationService
{
    private async Task<bool> RetryLearningArtifactsAsync(int userId, int storyId, string operation, CancellationToken ct)
    {
        return await InStoryTransactionAsync(storyId, async () =>
        {
            var story = await LoadAuthorizedStoryAsync(userId, storyId, ct);
            var version = await _unitOfWork.Repository<StoryVersion>().FirstOrDefaultAsync(x => x.StoryId == storyId && x.IsCurrent && x.Content != null, cancellationToken: ct);
            if (version is null) return false;
            await CreatorPublicationGuard.EnsureWritableAsync(_unitOfWork, storyId, version.Id, ct);
            var root = await _unitOfWork.Repository<StoryGenerationJob>().FirstOrDefaultAsync(x => x.StoryId == storyId &&
                x.StoryVersionId == version.Id && x.Operation == GenerationJobOperation.GenerateVocabulary, cancellationToken: ct);
            if (root is null) return false;
            await _learningBatches!.ValidateSourceAsync(root.Id, story, version, userId, ct);
            var hash = LearningBatchAccess.Hash(new { kind = "learning-retry", storyId, version.Id });
            var receipt = await _unitOfWork.Repository<StoryAuthoringOperation>().FirstOrDefaultAsync(x => x.StoryId == storyId && x.OperationId == operation, cancellationToken: ct);
            if (receipt is not null)
            {
                if (receipt.ActorUserId != userId || receipt.Kind != "learning-retry" || receipt.RequestHash != hash)
                    throw new ConflictException("LEARNING_RETRY_KEY_CONFLICT");
                return true;
            }
            if (await _unitOfWork.Repository<StoryGenerationJob>().ExistsAsync(x => x.StoryId == storyId &&
                (x.Status == GenerationJobStatus.Processing || x.Status == GenerationJobStatus.Pending), ct))
                throw new ConflictException("LEARNING_JOB_ACTIVE");
            var failed = (await _unitOfWork.Repository<StoryGenerationJob>().FindAsync(x => x.StoryVersionId == version.Id &&
                (x.Operation == GenerationJobOperation.GenerateVocabulary || x.Operation == GenerationJobOperation.GenerateQuiz || x.Operation == GenerationJobOperation.GenerateDiscussion) &&
                x.Status == GenerationJobStatus.Failed, cancellationToken: ct))
                .OrderByDescending(x => x.CompletedAt ?? x.StartedAt).ThenByDescending(x => x.Id).FirstOrDefault();
            if (failed is null) throw new ConflictException("LEARNING_JOB_NOT_FAILED");
            var batches = await _unitOfWork.Repository<LearningArtifactGenerationBatch>().FindAsync(x => x.RootJobId == root.Id, cancellationToken: ct);
            // Completed means that a batch passed its own checks, not necessarily the merged quiz checks.
            // Rebuild the rejected quiz package through its coordinator; leave validated vocabulary untouched.
            var rebuildQuiz = failed.Operation == GenerationJobOperation.GenerateQuiz && failed.ErrorCode == "QUIZ_VALIDATION_FAILED" &&
                batches.Any(x => x.Kind.StartsWith("quiz_", StringComparison.Ordinal));
            foreach (var batch in batches.Where(x => x.Status != GenerationJobStatus.Completed ||
                         rebuildQuiz && x.Kind.StartsWith("quiz_", StringComparison.Ordinal)))
            {
                _unitOfWork.Repository<LearningArtifactGenerationBatch>().Update(batch);
                batch.Status = GenerationJobStatus.Pending; batch.AttemptCount = 0;
                batch.ErrorCode = rebuildQuiz && batch.Kind.StartsWith("quiz_", StringComparison.Ordinal)
                    ? "QUIZ_VALIDATION_FAILED" : LearningValidationFeedback.Normalize(batch.ErrorCode);
                batch.LeaseExpiresAt = batch.NextAttemptAt = batch.CompletedAt = null;
                batch.ResultJson = null; batch.ShortfallReason = null;
                batch.ConcurrencyToken = Guid.NewGuid().ToString("N");
                _unitOfWork.Repository<LearningArtifactGenerationBatch>().Update(batch);
            }
            var resume = rebuildQuiz ? root : failed;
            // The quiz job stays Failed until the vocabulary coordinator has rebuilt all quiz batches.
            // This prevents a second worker from consuming a partially regenerated package.
            resume.Status = GenerationJobStatus.Pending; resume.AttemptNo = 0; resume.ErrorCode = null;
            resume.LeaseExpiresAt = resume.CompletedAt = null;
            _unitOfWork.Repository<StoryGenerationJob>().Update(resume);
            RotateToken(resume);
            _unitOfWork.Repository<StoryGenerationJob>().Update(resume);
            await _unitOfWork.Repository<StoryAuthoringOperation>().AddAsync(new()
            {
                StoryId = storyId, StoryVersionId = version.Id, ActorUserId = userId, OperationId = operation,
                Kind = "learning-retry", RequestHash = hash, OperationResponseJson = JsonSerializer.Serialize(new { failed.Id })
            }, ct);
            return true;
        }, ct);
    }
}
