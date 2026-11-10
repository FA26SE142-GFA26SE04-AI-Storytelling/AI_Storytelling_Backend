using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StoryPlatform.Application.Features.Payments.Interfaces;

namespace StoryPlatform.Infrastructure.BackgroundServices;

public sealed class SubscriptionCycleWorkerOptions
{
    public const string SectionName = "Payments:SubscriptionCycleWorker";
    public int IntervalMinutes { get; set; } = 15;
}

/// <summary>
/// Job định kỳ chuyển chu kỳ gói: hết hạn gói không gia hạn, kích hoạt chu kỳ đã gia hạn sớm
/// và gửi thông báo nhắc gia hạn trước 3 ngày (Luồng 2, 2g); sau đó dò giao dịch Paid chưa được áp gói (UC-L2-13).
/// </summary>
public sealed class SubscriptionCycleWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SubscriptionCycleWorker> _logger;
    private readonly SubscriptionCycleWorkerOptions _options;

    public SubscriptionCycleWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<SubscriptionCycleWorker> logger,
        IOptions<SubscriptionCycleWorkerOptions> options)
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
                var subscriptionService = scope.ServiceProvider.GetRequiredService<ISubscriptionService>();
                await subscriptionService.SweepAsync(DateTime.UtcNow, stoppingToken);

                // Sau khi chuyển chu kỳ: ghi nhận giao dịch Paid chưa được áp gói để Admin xử lý (UC-L2-13).
                var reconciliationService = scope.ServiceProvider.GetRequiredService<IPaymentReconciliationService>();
                await reconciliationService.DetectAsync(DateTime.UtcNow, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Subscription cycle sweep iteration failed.");
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
