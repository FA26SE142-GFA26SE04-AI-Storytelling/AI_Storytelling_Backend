using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StoryPlatform.Application.Features.MediaGeneration;
using StoryPlatform.Application.Features.MediaGeneration.Interfaces;
using StoryPlatform.Application.Features.MediaGeneration.Models;

namespace StoryPlatform.Infrastructure.BackgroundServices;

public sealed class MediaGenerationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MediaGenerationWorker> _logger;
    private readonly MediaGenerationOptions _options;

    public MediaGenerationWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<MediaGenerationWorker> logger,
        MediaGenerationOptions options)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.WorkerEnabled)
        {
            _logger.LogInformation("Phase 5 media worker is disabled until media providers are configured.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // IMPORTANT: Scope is created INSIDE the loop to prevent cross-iteration state leakage.
                // If this worker were registered as Singleton, this would be incorrect and could cause bugs.
                // The scoped service resolution here ensures fresh DbContext per iteration.
                MediaJobProcessResult result;
                await using (var scope = _scopeFactory.CreateAsyncScope())
                {
                    var processor = scope.ServiceProvider.GetRequiredService<IMediaGenerationJobProcessor>();
                    result = await processor.ProcessNextAsync(stoppingToken);
                }
                if (!result.JobFound)
                {
                    await Task.Delay(
                        TimeSpan.FromSeconds(Math.Clamp(_options.IdleDelaySeconds, 1, 30)), stoppingToken);
                }
                else if (!result.Success)
                {
                    var delaySeconds = result.ErrorCode == "MEDIA_CIRCUIT_OPEN"
                        ? Math.Clamp(_options.CircuitBreaker.DurationOfBreakSeconds, 10, 1800)
                        : result.IsPermanentFailure
                            ? Math.Clamp(_options.PermanentFailureDelaySeconds, 30, 1800)
                            : Math.Clamp(_options.TransientFailureDelaySeconds, 1, 60);
                    if (result.IsPermanentFailure)
                        _logger.LogWarning(
                            "Permanent Phase 5 media failure {ErrorCode}; retrying after {DelaySeconds} seconds.",
                            result.ErrorCode, delaySeconds);
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Phase 5 media generation worker iteration failed.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}
