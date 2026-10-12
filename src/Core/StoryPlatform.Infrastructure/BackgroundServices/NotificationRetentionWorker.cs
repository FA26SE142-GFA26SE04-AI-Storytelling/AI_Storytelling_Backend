using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StoryPlatform.Application.Features.Notifications.Interfaces;

namespace StoryPlatform.Infrastructure.BackgroundServices;

public sealed class NotificationRetentionWorkerOptions
{
    public const string SectionName = "Notifications:RetentionWorker";
    public int IntervalMinutes { get; set; } = 360;
}

/// <summary>Job định kỳ xóa thông báo quá 90 ngày.</summary>
public sealed class NotificationRetentionWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<NotificationRetentionWorker> _logger;
    private readonly NotificationRetentionWorkerOptions _options;

    public NotificationRetentionWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<NotificationRetentionWorker> logger,
        IOptions<NotificationRetentionWorkerOptions> options)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(
            Math.Clamp(_options.IntervalMinutes, 5, 1440));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider
                    .GetRequiredService<INotificationRetentionService>();
                var deleted = await service.PurgeExpiredAsync(
                    DateTime.UtcNow, stoppingToken);
                if (deleted > 0)
                {
                    _logger.LogInformation(
                        "Đã xóa {Count} thông báo quá hạn lưu giữ.", deleted);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception, "Notification retention iteration failed.");
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
