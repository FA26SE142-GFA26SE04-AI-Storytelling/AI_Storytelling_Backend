using StoryPlatform.Application.Features.AIStoryInput.Models;
using StoryPlatform.Application.Features.ListeningInteractions.Models;
using StoryPlatform.Application.Features.ListeningInteractions.Services;
using StoryPlatform.Application.Features.MediaGeneration.Models;
using StoryPlatform.Application.Features.MediaGeneration.Services;
using StoryPlatform.Contracts.AI.Models;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using System.Text.Json;
using StoryPlatform.Application.Abstractions.AI;

namespace StoryPlatform.Application.Features.ContentGeneration.Services;

/// <summary>Phase 4: sinh tương tác lúc nghe. Mỗi loại trong kế hoạch là một job, chạy lần lượt sau Quiz.</summary>
public sealed partial class ContentGenerationService
{
    private static readonly StoryBlockParser ListeningBlockParser = new();

    private async Task ProcessListeningWithLeaseAsync(int jobId, string token, CancellationToken ct)
    {
        if (_leaseRenewer is null) { await ProcessListeningAsync(jobId, token, ct); return; }
        using var work = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var renewalStop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        async Task RenewAsync()
        {
            try
            {
                while (true)
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(_jobLease.TotalSeconds / 3, 1, 60)), renewalStop.Token);
                    var retained = await _leaseRenewer.RenewAsync(jobId, token, _jobLease, renewalStop.Token);
                    renewalStop.Token.ThrowIfCancellationRequested();
                    if (!retained)
                    {
                        work.Cancel();
                        throw new InvalidOperationException("STALE_JOB_RESULT");
                    }
                }
            }
            catch (OperationCanceledException) when (renewalStop.IsCancellationRequested) { }
            catch { work.Cancel(); throw; }
        }
        var renewal = RenewAsync();
        try { await ProcessListeningAsync(jobId, token, work.Token, renewalStop.Cancel); }
        finally
        {
            renewalStop.Cancel();
            await renewal;
        }
    }

    private async Task ProcessListeningAsync(int jobId, string claimedToken, CancellationToken cancellationToken, Action? stopRenewal = null)
    {
        var state = await LoadArtifactStateAsync(jobId, GenerationJobOperation.GenerateListeningInteraction, cancellationToken);
        var type = state.Job.ListeningInteractionType ?? throw new InvalidOperationException("INVALID_PHASE3_HANDOFF");
        var versionId = state.Version.Id;
        if (!await _unitOfWork.Repository<StoryVocabulary>().ExistsAsync(x => x.ParentStoryPackageId == null && x.StoryVersionId == versionId, cancellationToken) ||
            !await _unitOfWork.Repository<QuizItem>().ExistsAsync(x => x.ParentStoryPackageId == null && x.StoryVersionId == versionId, cancellationToken))
            throw new InvalidOperationException("INVALID_PHASE3_HANDOFF");
        var scope = ListeningInteractionGenerator.CreateScope(state.Version, state.Context);
        if (scope.Plan.Items.All(x => x.Type != type)) throw new InvalidOperationException("INVALID_PHASE3_HANDOFF");
        IReadOnlyList<ListeningInteractionDraft> drafts = [];
        var metadata = new GenerationMetadataDto();
        if (!await HasListeningTypeAsync(versionId, type, cancellationToken))
        {
            var receipt = ReadListeningState(state.Job.GenerationMetadataJson);
            if (receipt.PinnedRequest is null) receipt.MaximumValidationAttempts = _artifactMaxAttempts;
            (drafts, metadata) = await _listeningGenerator.GenerateAsync(scope, type, $"listening-job-{state.Job.Id}", cancellationToken,
                receipt, (value, ct) => PersistListeningStateAsync(state, claimedToken, value, ct));
        }

        stopRenewal?.Invoke(); // Provider work is over; completion rotates the token and owns the short final transaction.
        await CompleteArtifactAsync(state, claimedToken, metadata, async () =>
        {
            if (!await HasListeningTypeAsync(versionId, type, cancellationToken))
                await _listeningGenerator.InsertAsync(versionId, drafts, cancellationToken);
            if (ListeningNext(scope.Plan, type) is null)
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                var inspection = await _listeningGenerator.InspectAsync(scope, cancellationToken);
                if (!inspection.Valid) throw new InvalidOperationException("LISTENING_PACKAGE_INVALID");
            }
        }, ListeningNext(scope.Plan, type), cancellationToken);
    }

    private static ListeningJobState ReadListeningState(string? json) => string.IsNullOrWhiteSpace(json)
        ? new() : JsonSerializer.Deserialize<ListeningJobState>(json, JsonOptions) ?? throw new InvalidOperationException("LISTENING_SNAPSHOT_INVALID");

    private Task<bool> PersistListeningStateAsync(ArtifactState state, string token, ListeningJobState value, CancellationToken ct) =>
        InStoryTransactionAsync(state.Job.StoryId, async () =>
        {
            var job = await RequireClaimedJobAsync(state.Job.Id, token, ct);
            var version = await _unitOfWork.Repository<StoryVersion>().FirstOrDefaultAsync(x => x.Id == state.Version.Id && x.IsCurrent, cancellationToken: ct);
            if (version is null || ListeningJobState.Fingerprint(ListeningInteractionGenerator.CreateScope(version, state.Context)) != value.SourceFingerprint)
                throw new InvalidOperationException("LISTENING_SOURCE_STALE");
            if (value.PinnedRequest?.Snapshot is { } snapshot && job.PromptCatalogVersionId is int catalogId)
                value.PinnedRequest = value.PinnedRequest with { Snapshot = snapshot with { PromptCatalogVersionId = catalogId } };
            job.GenerationMetadataJson = JsonSerializer.Serialize(value, JsonOptions);
            _unitOfWork.Repository<StoryGenerationJob>().Update(job);
            return true;
        }, ct);

    private async Task<bool> DeferListeningAsync(StoryGenerationJob observed, string token, Exception error, CancellationToken ct)
    {
        if (observed.Operation != GenerationJobOperation.GenerateListeningInteraction) return false;
        var transient = error is TimeoutException or TaskCanceledException || error is HttpRequestException { StatusCode: null } ||
            error is AIServiceRequestException ai && ai.ErrorCode != "AI_OUTPUT_TRUNCATED" && ai.StatusCode != 501 &&
            (ai.StatusCode is 408 or 429 || ai.StatusCode >= 500 && ai.ErrorCode != "AI_OUTPUT_INVALID");
        if (!transient) return false;
        return await InStoryTransactionAsync(observed.StoryId, async () =>
        {
            var job = await RequireClaimedJobAsync(observed.Id, token, ct);
            var state = ReadListeningState(job.GenerationMetadataJson);
            if (job.AttemptNo >= Math.Max(3, job.MaxAttempts) || state.ProviderCalls >= 3) return false;
            var rateLimited = error is AIServiceRequestException { StatusCode: 429 };
            var hint = (error as AIServiceRequestException)?.RetryAfter;
            var delay = hint is { } duration && duration > TimeSpan.Zero ? duration.TotalSeconds :
                rateLimited ? 60 : 2 * Math.Pow(2, Math.Min(job.AttemptNo, 5)) + Random.Shared.NextDouble();
            job.Status = GenerationJobStatus.Pending;
            job.Stage = JobStage.ContentArtifactPending;
            job.ErrorCode = rateLimited ? "LISTENING_PROVIDER_RATE_LIMITED" : "LISTENING_PROVIDER_UNAVAILABLE";
            job.NextAttemptAt = DateTime.UtcNow.AddSeconds(Math.Clamp(delay, 1, 86400));
            job.LeaseExpiresAt = null;
            job.MaxAttempts = Math.Max(3, job.MaxAttempts);
            _unitOfWork.Repository<StoryGenerationJob>().Update(job);
            RotateToken(job);
            _unitOfWork.Repository<StoryGenerationJob>().Update(job);
            return true;
        }, ct);
    }

    private Task<bool> HasListeningTypeAsync(int versionId, ListeningInteractionType type, CancellationToken cancellationToken) =>
        _unitOfWork.Repository<ListeningInteraction>().ExistsAsync(
            x => x.ParentStoryPackageId == null && x.StoryVersionId == versionId && x.Type == type && !x.IsDeleted, cancellationToken);

    private static ListeningInteractionPlan ListeningPlan(AIStoryInputContextSnapshot context, IReadOnlyList<StoryTextBlock> blocks) =>
        ListeningInteractionPlanner.Create(ListeningInteractionGenerator.ToAgeBand(context.AgeBand), blocks);

    /// <summary>Job kế tiếp sau loại after (null = ngay sau Quiz); null khi đã hết kế hoạch, truyện sang ContentReview.</summary>
    private static NextArtifact? ListeningNext(ListeningInteractionPlan plan, ListeningInteractionType? after)
    {
        var index = after is null ? 0 : plan.Items.Select(x => x.Type).ToList().IndexOf(after.Value) + 1;
        return index < plan.Items.Count ? new(GenerationJobOperation.GenerateListeningInteraction, plan.Items[index].Type) : null;
    }


}
