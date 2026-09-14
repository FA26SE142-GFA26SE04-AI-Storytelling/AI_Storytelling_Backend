using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StoryPlatform.Application.Features.Outline.Interfaces;

namespace StoryPlatform.Infrastructure.BackgroundServices;

public sealed class OutlineWorkerOptions
{
    public const string SectionName = "AI:OutlineWorker";
    public int IdleDelaySeconds { get; set; } = 2;
    public int ErrorDelaySeconds { get; set; } = 5;
}

public sealed class OutlineGenerationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutlineGenerationWorker> _logger;
    private readonly OutlineWorkerOptions _options;

    public OutlineGenerationWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<OutlineGenerationWorker> logger,
        IOptions<OutlineWorkerOptions> options)
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
                var processor = scope.ServiceProvider.GetRequiredService<IOutlineJobProcessor>();
                var processed = await processor.ProcessNextAsync(stoppingToken);
                if (!processed)
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(_options.IdleDelaySeconds, 1, 30)), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Outline worker iteration failed.");
                await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(_options.ErrorDelaySeconds, 1, 60)), stoppingToken);
            }
        }
    }
}
