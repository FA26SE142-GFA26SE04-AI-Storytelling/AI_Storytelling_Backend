using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.Outline.Interfaces;
using StoryPlatform.Application.Features.TokenQuota.Services;
using StoryPlatform.Domain.Enums;
using StoryPlatform.Infrastructure.Persistence;

namespace StoryPlatform.Infrastructure.BackgroundServices;

public sealed class OutlineJobFailureFinalizer : IOutlineJobFailureFinalizer
{
    private readonly IServiceScopeFactory _scopeFactory;

    public OutlineJobFailureFinalizer(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task MarkFailedAsync(
        int jobId,
        string expectedConcurrencyToken,
        string errorCode,
        CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        // Cùng DbContext của scope: đánh dấu job lỗi và trả lượt nằm trong một transaction.
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var job = await context.StoryGenerationJobs.FirstOrDefaultAsync(
                item => item.Id == jobId &&
                        item.Status == GenerationJobStatus.Processing &&
                        item.ConcurrencyToken == expectedConcurrencyToken,
                cancellationToken);
            if (job is null)
            {
                await unitOfWork.RollbackTransactionAsync(cancellationToken);
                return;
            }

            job.Status = GenerationJobStatus.Failed;
            job.Stage = JobStage.OutlineFailed;
            job.ErrorCode = errorCode;
            job.FallbackMessage = "Không thể tạo outline an toàn. Vui lòng thử lại hoặc điều chỉnh input.";
            job.CompletedAt = DateTime.UtcNow;
            job.LeaseExpiresAt = null;
            job.ConcurrencyToken = Guid.NewGuid().ToString("N");
            // Chỉ lần sinh dàn ý đầu tiên lỗi mới trả lượt; sinh lại dàn ý lỗi thì truyện vẫn còn dàn ý cũ để đi tiếp.
            if (job.Operation == GenerationJobOperation.GenerateOutline)
            {
                await TokenQuotaService.ReleaseStoryCreditAsync(unitOfWork, job.StoryId, cancellationToken);
            }

            await unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            throw;
        }
    }
}
