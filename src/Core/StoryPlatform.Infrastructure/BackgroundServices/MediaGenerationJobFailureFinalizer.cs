using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StoryPlatform.Application.Features.MediaGeneration.Interfaces;
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
    }
}
