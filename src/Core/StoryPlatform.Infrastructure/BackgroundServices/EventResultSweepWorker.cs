using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StoryPlatform.Application.Features.Events.Interfaces;

namespace StoryPlatform.Infrastructure.BackgroundServices;

public sealed class EventResultSweepWorkerOptions
{
    public const string SectionName = "Events:ResultSweepWorker";
    public int IntervalMinutes { get; set; } = 5;
}

/// <summary>
/// Job định kỳ: tự xác minh người tham gia và lập danh sách tạm người nhận giải
/// cho các sự kiện theo hoạt động đã kết thúc thời gian thực hiện.
/// </summary>
public sealed class EventResultSweepWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EventResultSweepWorker> _logger;
    private readonly EventResultSweepWorkerOptions _options;

    public EventResultSweepWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<EventResultSweepWorker> logger,
        IOptions<EventResultSweepWorkerOptions> options)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(Math.Clamp(_options.IntervalMinutes, 1, 60));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var sweepService = scope.ServiceProvider.GetRequiredService<IEventResultSweepService>();
                await sweepService.SweepEndedEventsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Event result sweep iteration failed.");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
