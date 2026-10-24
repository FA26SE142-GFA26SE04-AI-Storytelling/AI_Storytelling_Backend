using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StoryPlatform.Application.Features.MediaGeneration;
using StoryPlatform.Application.Features.MediaGeneration.Interfaces;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Infrastructure.BackgroundServices;

public abstract class MediaBranchGenerationWorker(IServiceScopeFactory scopes, MediaGenerationOptions options,
    ILogger logger, MediaType type) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.WorkerEnabled) return Task.CompletedTask;
        var branch = type == MediaType.Illustration ? options.Images : options.Audio;
        return Task.WhenAll(Enumerable.Range(0, Math.Clamp(branch.Concurrency, 1, 8)).Select(_ => RunAsync(stoppingToken)));
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var processor = scope.ServiceProvider.GetRequiredService<IMediaGenerationJobProcessor>();
                var result = await processor.ProcessBranchNextAsync(type, ct);
                if (!result.JobFound || !result.Success)
                    await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(options.IdleDelaySeconds, 1, 30)), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Media branch {Type} worker failed", type);
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
        }
    }
}

public sealed class ImageGenerationWorker(IServiceScopeFactory scopes, MediaGenerationOptions options, ILogger<ImageGenerationWorker> logger)
    : MediaBranchGenerationWorker(scopes, options, logger, MediaType.Illustration);

public sealed class TtsGenerationWorker(IServiceScopeFactory scopes, MediaGenerationOptions options, ILogger<TtsGenerationWorker> logger)
    : MediaBranchGenerationWorker(scopes, options, logger, MediaType.TtsAudio);
