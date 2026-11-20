using StoryPlatform.Application.Abstractions.AI;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.Moderation.Services;

/// <summary>Terminalizes abandoned inline checks; never dispatches the provider again.</summary>
public sealed class GrayZoneCheckRecoveryService(IUnitOfWork uow, IAccountAiActivityService aiActivity)
{
    public static readonly TimeSpan ExecutionTimeout = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan RecoveryGrace = TimeSpan.FromMinutes(1);

    public async Task RecoverAsync(CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow - RecoveryGrace;
        var candidates = await uow.Repository<StoryGenerationJob>().GetPagedAsync(1, 100, j => !j.IsDeleted &&
            j.Operation == GenerationJobOperation.GrayZoneCheck && j.Status == GenerationJobStatus.Processing &&
            j.LeaseExpiresAt != null && j.LeaseExpiresAt <= cutoff,
            orderBy: q => q.OrderBy(j => j.LeaseExpiresAt).ThenBy(j => j.Id), cancellationToken: ct);
        foreach (var candidate in candidates.Items)
        {
            var owner = await uow.Repository<Story>().FirstOrDefaultAsync(s => s.Id == candidate.StoryId, cancellationToken: ct);
            if (owner is null) continue;
            await uow.BeginTransactionAsync(ct);
            try
            {
                await uow.AcquireAccountAiActivityLockAsync(owner.AuthorUserId, ct);
                await uow.AcquireTransactionLockAsync(candidate.StoryId, ct);
                var job = await uow.Repository<StoryGenerationJob>().FirstOrDefaultAsync(j => j.Id == candidate.Id &&
                    !j.IsDeleted && j.Operation == GenerationJobOperation.GrayZoneCheck &&
                    j.Status == GenerationJobStatus.Processing && j.ConcurrencyToken == candidate.ConcurrencyToken &&
                    j.LeaseExpiresAt != null && j.LeaseExpiresAt <= cutoff, cancellationToken: ct);
                if (job is not null)
                {
                    // Attach the observed token before rotating it (repository reads are detached).
                    uow.Repository<StoryGenerationJob>().Update(job);
                    job.Status = GenerationJobStatus.Failed;
                    job.ErrorCode = "AI_OPERATION_INTERRUPTED";
                    job.CompletedAt = DateTime.UtcNow;
                    job.LeaseExpiresAt = null;
                    job.ConcurrencyToken = Guid.NewGuid().ToString("N");
                    uow.Repository<StoryGenerationJob>().Update(job);
                    await uow.SaveChangesAsync(ct);
                    await aiActivity.ReconcileStoryAiActivityAsync(owner.AuthorUserId, candidate.StoryId, ct);
                }
                await uow.CommitTransactionAsync(ct);
            }
            catch
            {
                await uow.RollbackTransactionAsync(CancellationToken.None);
                uow.DiscardTrackedChanges();
                throw;
            }
        }
    }
}
