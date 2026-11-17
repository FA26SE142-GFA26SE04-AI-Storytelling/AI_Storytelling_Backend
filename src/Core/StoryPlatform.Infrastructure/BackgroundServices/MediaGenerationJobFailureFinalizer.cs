using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.MediaGeneration.Interfaces;
using StoryPlatform.Application.Features.TokenQuota.Services;
using StoryPlatform.Domain.Enums;
using StoryPlatform.Infrastructure.Persistence;

namespace StoryPlatform.Infrastructure.BackgroundServices;

public sealed class MediaGenerationJobFailureFinalizer : IMediaGenerationJobFailureFinalizer
{
    private readonly IServiceScopeFactory _scopeFactory;
    public MediaGenerationJobFailureFinalizer(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    public async Task MarkFailedAsync(
        int jobId, string expectedConcurrencyToken, string errorCode, CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var job = await context.StoryGenerationJobs.FirstOrDefaultAsync(x =>
            x.Id == jobId && x.Status != GenerationJobStatus.Completed &&
            x.Status != GenerationJobStatus.Cancelled &&
            x.ConcurrencyToken == expectedConcurrencyToken, cancellationToken);
        if (job is null) return;
        job.Status = GenerationJobStatus.Failed;
        job.Stage = JobStage.MediaFailed;
        job.ErrorCode = errorCode;
        job.FallbackMessage = "Không thể hoàn tất media. Nội dung truyện và các asset đã hoàn thành vẫn được giữ nguyên.";
        job.CompletedAt = DateTime.UtcNow;
        job.LeaseExpiresAt = null;
        job.ConcurrencyToken = Guid.NewGuid().ToString("N");
        await context.SaveChangesAsync(cancellationToken);

        // Media lỗi hẳn: trả lượt đang giữ của truyện viết tay (Luồng 2). Truyện AI đã trừ lượt lúc sinh nội dung nên
        // không còn lượt giữ; truyện còn job media khác đang chạy thì giữ lượt cho job đó.
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await unitOfWork.AcquireTransactionLockAsync(job.StoryId, cancellationToken);
            await TokenQuotaService.ReleaseStoryCreditIfIdleAsync(unitOfWork, job.StoryId, cancellationToken);
            await unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            throw;
        }
    }
}
