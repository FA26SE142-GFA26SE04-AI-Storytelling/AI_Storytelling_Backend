using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.ContentGeneration.Interfaces;
using StoryPlatform.Application.Features.TokenQuota.Services;
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
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        // Cùng DbContext của scope: đánh dấu job lỗi và trả lượt nằm trong một transaction.
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var job = await context.StoryGenerationJobs.FirstOrDefaultAsync(item =>
                item.Id == jobId && item.Status == GenerationJobStatus.Processing &&
                item.ConcurrencyToken == expectedConcurrencyToken, cancellationToken);
            if (job is null)
            {
                await unitOfWork.RollbackTransactionAsync(cancellationToken);
                return;
            }

            job.Status = GenerationJobStatus.Failed;
            job.Stage = JobStage.ContentFailed;
            job.ErrorCode = errorCode;
            job.FallbackMessage = "Không thể hoàn tất gói nội dung. Hệ thống đã giữ lại các phần hoàn thành trước đó.";
            job.CompletedAt = DateTime.UtcNow;
            job.LeaseExpiresAt = null;
            job.ConcurrencyToken = Guid.NewGuid().ToString("N");
            // Lỗi trước khi AI sinh xong nội dung: trả lượt đang giữ. Lỗi ở từ vựng, quiz, tương tác (lượt đã trừ) thì
            // không có lượt đang giữ nên không đổi gì.
            await TokenQuotaService.ReleaseStoryCreditAsync(unitOfWork, job.StoryId, cancellationToken);
            await unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            throw;
        }
    }
}
