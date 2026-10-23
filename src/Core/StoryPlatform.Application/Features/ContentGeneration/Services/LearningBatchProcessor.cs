using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using StoryPlatform.Application.Abstractions.AI;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ChildProfiles.Supervision.Interfaces;
using StoryPlatform.Application.Features.ContentGeneration.Interfaces;
using StoryPlatform.Contracts.AI.Models;
using StoryPlatform.Contracts.AI.Responses;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ContentGeneration.Services;

public sealed class LearningBatchProcessor(IUnitOfWork uow, IAIStoryGenerationClient ai, ISupervisionAccessGuard guard,
    ILearningRequestGate gate, LearningBatchOptions options, ILogger<LearningBatchProcessor> logger) : ILearningBatchProcessor
{
    private TimeSpan Lease => TimeSpan.FromMinutes(Math.Clamp(options.LeaseMinutes, 8, 30));
    public async Task ProcessAsync(int batchId, string rootClaimToken, CancellationToken ct)
    {
        for (var iteration = 0; iteration < Math.Clamp(options.MaxAttempts, 1, 3); iteration++)
        {
            var observed = await Fresh<LearningArtifactGenerationBatch>(batchId, ct) ?? throw new ConflictException("LEARNING_BATCH_NOT_FOUND");
            if (observed.Status == GenerationJobStatus.Completed) return;
            if (observed.NextAttemptAt is DateTime next && next > DateTime.UtcNow)
                await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(0, (next - DateTime.UtcNow).TotalMilliseconds)), ct);
            // The singleton gate covers all batches on this node; the DB claim below covers each story across nodes.
            var gateTimer = Stopwatch.StartNew();
            using var permit = await gate.EnterAsync(ct);
            logger.LogInformation("Learning batch gate Batch={BatchId} WaitMs={WaitMs}", batchId, gateTimer.Elapsed.TotalMilliseconds);
            LearningArtifactGenerationBatch batch;
            await uow.BeginTransactionAsync(ct);
            try
            {
                await uow.AcquireTransactionLockAsync(observed.StoryId, ct);
                batch = await Fresh<LearningArtifactGenerationBatch>(batchId, ct) ?? throw new ConflictException("LEARNING_BATCH_NOT_FOUND");
                await AssertSourceAsync(batch, rootClaimToken, ct);
                if (batch.Status == GenerationJobStatus.Completed) { await uow.CommitTransactionAsync(ct); return; }
                if (batch.AttemptCount >= Math.Clamp(options.MaxAttempts, 1, 3)) throw new ConflictException("LEARNING_BATCH_RETRY_EXHAUSTED");
                if (batch.Status == GenerationJobStatus.Processing && batch.RootClaimToken == rootClaimToken && batch.LeaseExpiresAt > DateTime.UtcNow)
                    throw new ConflictException("LEARNING_BATCH_ACTIVE");
                var active = await uow.Repository<LearningArtifactGenerationBatch>().CountAsync(x => x.StoryId == batch.StoryId &&
                    x.Id != batch.Id && x.Status == GenerationJobStatus.Processing && x.LeaseExpiresAt > DateTime.UtcNow, ct);
                if (active >= Math.Clamp(options.MaxConcurrentPerStory, 1, 2)) throw new ConflictException("LEARNING_STORY_CONCURRENCY_LIMIT");
                uow.Repository<LearningArtifactGenerationBatch>().Update(batch);
                batch.ConcurrencyToken = Guid.NewGuid().ToString("N");
                batch.RootClaimToken = rootClaimToken;
                batch.Status = GenerationJobStatus.Processing;
                batch.AttemptCount++;
                batch.NextAttemptAt = null;
                batch.LeaseExpiresAt = DateTime.UtcNow.Add(Lease);
                uow.Repository<LearningArtifactGenerationBatch>().Update(batch);
                var root = await Fresh<StoryGenerationJob>(batch.RootJobId, ct) ?? throw new ConflictException("STALE_LEARNING_JOB");
                root.LeaseExpiresAt = DateTime.UtcNow.Add(Lease);
                uow.Repository<StoryGenerationJob>().Update(root);
                await uow.CommitTransactionAsync(ct);
            }
            catch { await Rollback(ct); throw; }

            var token = batch.ConcurrencyToken;
            var timer = Stopwatch.StartNew();
            var request = JsonSerializer.Deserialize<LearningBatchRequest>(batch.RequestJson, LearningBatchService.Json)!;
            string? code = null;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 10, 120)));
                string result, reason;
                if (request.Vocabulary is { } vocabulary)
                {
                    var response = await ai.GenerateVocabularyAsync(vocabulary, timeout.Token);
                    LearningArtifactGrounding.Vocabulary(vocabulary.Story, response.Items, vocabulary.Batch!, response.ShortfallReason);
                    ValidateSafety(string.Join(' ', response.Items.Select(x => $"{x.Term} {x.Definition} {x.SourceQuote} {x.Example}")), request);
                    result = JsonSerializer.Serialize(response, LearningBatchService.Json); reason = response.ShortfallReason;
                }
                else
                {
                    var quiz = request.Quiz!;
                    var response = await ai.GenerateQuizAsync(quiz, timeout.Token);
                    LearningArtifactGrounding.Quiz(quiz.Story, response.Items, quiz.Batch!, response.ShortfallReason, quiz.Vocabulary);
                    ValidateSafety(string.Join(' ', response.Items.Select(x => $"{x.Question} {x.CorrectAnswer} {x.Explanation} {x.Hint} {string.Join(' ', x.Options)} {string.Join(' ', x.OptionFeedback)}")), request);
                    result = JsonSerializer.Serialize(response, LearningBatchService.Json); reason = response.ShortfallReason;
                }
                timeout.Token.ThrowIfCancellationRequested();
                await uow.BeginTransactionAsync(ct);
                try
                {
                    await uow.AcquireTransactionLockAsync(batch.StoryId, ct);
                    var fresh = await RequireClaimAsync(batch.Id, token, ct);
                    await AssertSourceAsync(fresh, rootClaimToken, ct);
                    fresh.ResultJson = result; fresh.ShortfallReason = reason; fresh.ErrorCode = null;
                    fresh.Status = GenerationJobStatus.Completed; fresh.CompletedAt = DateTime.UtcNow; fresh.LeaseExpiresAt = null;
                    uow.Repository<LearningArtifactGenerationBatch>().Update(fresh);
                    await uow.CommitTransactionAsync(ct);
                }
                catch { await Rollback(ct); throw; }
                return;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                code = ex is AIServiceRequestException remote ? remote.ErrorCode : ex is JsonException ? "LEARNING_OUTPUT_INVALID" :
                    ex is OperationCanceledException or TimeoutException ? "LEARNING_TIMEOUT" : "LEARNING_BATCH_FAILED";
                var retryable = ex is JsonException or HttpRequestException or TimeoutException or OperationCanceledException ||
                    ex is AIServiceRequestException remoteError && remoteError.StatusCode is 408 or 429 or >= 500;
                await uow.BeginTransactionAsync(ct);
                try
                {
                    await uow.AcquireTransactionLockAsync(batch.StoryId, ct);
                    var fresh = await Fresh<LearningArtifactGenerationBatch>(batch.Id, ct);
                    if (fresh?.ConcurrencyToken != token || fresh.Status != GenerationJobStatus.Processing || fresh.LeaseExpiresAt <= DateTime.UtcNow)
                        throw new ConflictException("STALE_LEARNING_BATCH");
                    fresh.Status = retryable && fresh.AttemptCount < Math.Clamp(options.MaxAttempts, 1, 3) ? GenerationJobStatus.Pending : GenerationJobStatus.Failed;
                    fresh.ErrorCode = code;
                    fresh.LeaseExpiresAt = null;
                    var delay = TimeSpan.FromSeconds(Math.Max(0, options.BaseRetryDelaySeconds) * Math.Pow(2, fresh.AttemptCount - 1));
                    if (ex is AIServiceRequestException { RetryAfter: { } after } && after > delay) delay = after;
                    fresh.NextAttemptAt = fresh.Status == GenerationJobStatus.Pending ? DateTime.UtcNow.Add(delay) : null;
                    uow.Repository<LearningArtifactGenerationBatch>().Update(fresh);
                    await uow.CommitTransactionAsync(ct);
                    if (fresh.Status == GenerationJobStatus.Failed) throw new InvalidOperationException(code);
                }
                catch { await Rollback(CancellationToken.None); throw; }
            }
            finally
            {
                logger.LogInformation("Learning batch Root={RootJobId} Batch={BatchId} Story={StoryId} Version={VersionId} Kind={Kind} Attempt={Attempt} DurationMs={DurationMs} ErrorCode={ErrorCode}",
                    batch.RootJobId, batch.Id, batch.StoryId, batch.StoryVersionId, batch.Kind, batch.AttemptCount, timer.Elapsed.TotalMilliseconds, code);
            }
        }
        throw new InvalidOperationException("LEARNING_BATCH_RETRY_EXHAUSTED");
    }

    private Task<T?> Fresh<T>(int id, CancellationToken ct) where T : BaseEntity => uow.Repository<T>().FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, cancellationToken: ct);
    private async Task Rollback(CancellationToken ct) { try { await uow.RollbackTransactionAsync(ct); } finally { uow.DiscardTrackedChanges(); } }
    private async Task<LearningArtifactGenerationBatch> RequireClaimAsync(int id, string token, CancellationToken ct)
    {
        var row = await Fresh<LearningArtifactGenerationBatch>(id, ct);
        if (row?.Status != GenerationJobStatus.Processing || row.ConcurrencyToken != token || row.LeaseExpiresAt <= DateTime.UtcNow)
            throw new ConflictException("STALE_LEARNING_BATCH");
        return row;
    }
    private async Task AssertSourceAsync(LearningArtifactGenerationBatch batch, string rootToken, CancellationToken ct)
    {
        var root = await Fresh<StoryGenerationJob>(batch.RootJobId, ct);
        var story = await Fresh<Story>(batch.StoryId, ct) ?? throw new ConflictException("STALE_LEARNING_SOURCE");
        var version = await Fresh<StoryVersion>(batch.StoryVersionId, ct) ?? throw new ConflictException("STALE_LEARNING_SOURCE");
        if (root?.Status != GenerationJobStatus.Processing || root.ConcurrencyToken != rootToken || root.LeaseExpiresAt <= DateTime.UtcNow ||
            root.StoryVersionId != version.Id || root.StoryId != story.Id || LearningBatchAccess.Source(story, version) != batch.SourceFingerprint)
            throw new ConflictException("STALE_LEARNING_SOURCE");
        if (await LearningBatchAccess.AuthorizeAsync(uow, guard, story, version, batch.RequestedByUserId, ct) != batch.ContextFingerprint)
            throw new ConflictException("STALE_LEARNING_CONTEXT");
    }
    private static void ValidateSafety(string text, LearningBatchRequest request)
    {
        var blocked = (request.Context.BlockedCategoryTerms ?? request.Context.BlockedCategoryCodes)
            .Concat(request.Context.RestrictedCategoryTerms ?? request.Context.RestrictedCategoryCodes);
        if (blocked.Any(term => LearningArtifactGrounding.ContainsTerm(text, term)) ||
            Regex.IsMatch(text, @"\b[\w.%+-]+@[\w.-]+\.[A-Za-z]{2,}\b") || Regex.IsMatch(text, @"(?<!\d)(?:\+?84|0)(?:[ .-]?\d){9,10}(?!\d)"))
            throw new JsonException("LEARNING_SAFETY_INVALID");
    }
}
