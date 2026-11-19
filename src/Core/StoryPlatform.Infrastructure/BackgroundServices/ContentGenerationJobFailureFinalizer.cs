using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StoryPlatform.Application.Abstractions.AI;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.AiActivity.Services;
using StoryPlatform.Application.Features.ContentGeneration.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.Infrastructure.Persistence;

namespace StoryPlatform.Infrastructure.BackgroundServices;

public sealed class ContentGenerationJobFailureFinalizer : IContentGenerationJobFailureFinalizer, IContentGenerationJobLease
{
    private readonly IServiceScopeFactory _scopeFactory;
    public ContentGenerationJobFailureFinalizer(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    public async Task<bool> RenewAsync(int jobId, string expectedToken, TimeSpan lease, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = DateTime.UtcNow;
        return await context.StoryGenerationJobs.Where(x => x.Id == jobId && x.Status == GenerationJobStatus.Processing &&
            x.ConcurrencyToken == expectedToken && x.LeaseExpiresAt > now)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.LeaseExpiresAt, now.Add(lease)), ct) == 1;
    }

    public async Task MarkFailedAsync(
        int jobId, string expectedConcurrencyToken, string errorCode, CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var unitOfWork = scope.ServiceProvider.GetService<IUnitOfWork>();
        if (unitOfWork is null)
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var job = await context.StoryGenerationJobs.FirstOrDefaultAsync(item =>
                item.Id == jobId && item.Status == GenerationJobStatus.Processing &&
                item.ConcurrencyToken == expectedConcurrencyToken, cancellationToken);
            if (job is null) return;
            job.Status = GenerationJobStatus.Failed;
            job.Stage = JobStage.ContentFailed;
            job.ErrorCode = errorCode;
            job.FallbackMessage = "Không thể hoàn tất gói nội dung. Hệ thống đã giữ lại các phần hoàn thành trước đó.";
            job.CompletedAt = DateTime.UtcNow;
            job.LeaseExpiresAt = null;
            job.ConcurrencyToken = Guid.NewGuid().ToString("N");
            await context.SaveChangesAsync(cancellationToken);
            return;
        }

        var jobInfo = await unitOfWork.Repository<StoryGenerationJob>().FirstOrDefaultAsync(item =>
            item.Id == jobId && item.Status == GenerationJobStatus.Processing &&
            item.ConcurrencyToken == expectedConcurrencyToken, cancellationToken: cancellationToken);
        if (jobInfo is null) return;
        var storyInfo = await unitOfWork.Repository<Story>().FirstOrDefaultAsync(
            s => s.Id == jobInfo.StoryId, cancellationToken: cancellationToken);
        if (storyInfo is null) return;

        var aiActivity = scope.ServiceProvider.GetService<IAccountAiActivityService>()
            ?? new AccountAiActivityService(unitOfWork);

        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await unitOfWork.AcquireAccountAiActivityLockAsync(storyInfo.AuthorUserId, cancellationToken);
            await unitOfWork.AcquireTransactionLockAsync(jobInfo.StoryId, cancellationToken);

            var job = await unitOfWork.Repository<StoryGenerationJob>().FirstOrDefaultAsync(item =>
                item.Id == jobId && item.Status == GenerationJobStatus.Processing &&
                item.ConcurrencyToken == expectedConcurrencyToken, cancellationToken: cancellationToken);
            if (job is null)
            {
                await unitOfWork.CommitTransactionAsync(cancellationToken);
                return;
            }

            unitOfWork.Repository<StoryGenerationJob>().Update(job); // Preserve the observed concurrency token.
            job.Status = GenerationJobStatus.Failed;
            job.Stage = JobStage.ContentFailed;
            job.ErrorCode = errorCode;
            job.FallbackMessage = "Không thể hoàn tất gói nội dung. Hệ thống đã giữ lại các phần hoàn thành trước đó.";
            job.CompletedAt = DateTime.UtcNow;
            job.LeaseExpiresAt = null;
            job.ConcurrencyToken = Guid.NewGuid().ToString("N");
            unitOfWork.Repository<StoryGenerationJob>().Update(job);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await aiActivity.ReconcileStoryAiActivityAsync(storyInfo.AuthorUserId, jobInfo.StoryId, cancellationToken, failedJobId: job.Id);
            await unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            throw;
        }
    }
}
