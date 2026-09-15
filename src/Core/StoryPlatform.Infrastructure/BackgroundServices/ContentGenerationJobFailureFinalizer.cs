using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StoryPlatform.Application.Features.ContentGeneration.Interfaces;
using StoryPlatform.Domain.Enums;
using StoryPlatform.Infrastructure.Persistence;

namespace StoryPlatform.Infrastructure.BackgroundServices;

public sealed class ContentGenerationJobFailureFinalizer : IContentGenerationJobFailureFinalizer
{
    private readonly IServiceScopeFactory _scopeFactory;
    public ContentGenerationJobFailureFinalizer(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    public async Task MarkFailedAsync(
        int jobId, string expectedConcurrencyToken, string errorCode, CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
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
    }
}
