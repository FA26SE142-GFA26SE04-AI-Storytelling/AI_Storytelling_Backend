using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StoryPlatform.Application.Features.ContentGeneration.Interfaces;

namespace StoryPlatform.Infrastructure.BackgroundServices;

public sealed class ContentGenerationWorkerOptions
{
    public const string SectionName = "AI:ContentGenerationWorker";
    public int IdleDelaySeconds { get; set; } = 2;
    public int ErrorDelaySeconds { get; set; } = 5;
}

public sealed class ContentGenerationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ContentGenerationWorker> _logger;
    private readonly ContentGenerationWorkerOptions _options;

    public ContentGenerationWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<ContentGenerationWorker> logger,
        IOptions<ContentGenerationWorkerOptions> options)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var processor = scope.ServiceProvider.GetRequiredService<IContentGenerationJobProcessor>();
                if (!await processor.ProcessNextAsync(stoppingToken))
                    await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(_options.IdleDelaySeconds, 1, 30)), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Phase 3 content generation worker iteration failed.");
                await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(_options.ErrorDelaySeconds, 1, 60)), stoppingToken);
            }
        }
    }
}
