using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StoryPlatform.Application.Features.Payments.Interfaces;

namespace StoryPlatform.Infrastructure.BackgroundServices;

public sealed class PaymentExpirySweepWorkerOptions
{
    public const string SectionName = "Payments:ExpirySweepWorker";
    public int IntervalMinutes { get; set; } = 5;
}

/// <summary>
/// Job định kỳ chuyển payment_transactions Pending quá hạn sang Expired (Bước 5.6.3).
/// </summary>
public sealed class PaymentExpirySweepWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PaymentExpirySweepWorker> _logger;
    private readonly PaymentExpirySweepWorkerOptions _options;

    public PaymentExpirySweepWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<PaymentExpirySweepWorker> logger,
        IOptions<PaymentExpirySweepWorkerOptions> options)
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
                var sweepService = scope.ServiceProvider.GetRequiredService<IPaymentExpirySweepService>();
                await sweepService.SweepExpiredAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Payment expiry sweep iteration failed.");
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
