using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StoryPlatform.Application.Features.Moderation.Interfaces;
using StoryPlatform.Application.Features.Moderation.Services;

namespace StoryPlatform.Infrastructure.BackgroundServices;

public sealed class ModerationSweepWorkerOptions
{
    public const string SectionName = "Moderation:SweepWorker";
    public int IntervalMinutes { get; set; } = 5;
}

/// <summary>
/// Job định kỳ: trả mục kiểm duyệt bị giữ quá 2 giờ về hàng đợi và cảnh báo mục quá hạn SLA.
/// </summary>
public sealed class ModerationSweepWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ModerationSweepWorker> _logger;
    private readonly ModerationSweepWorkerOptions _options;

    public ModerationSweepWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<ModerationSweepWorker> logger,
        IOptions<ModerationSweepWorkerOptions> options)
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
                var sweepService = scope.ServiceProvider.GetRequiredService<IModerationSweepService>();
                await scope.ServiceProvider.GetRequiredService<GrayZoneWorkflowService>().CloseObsoleteAsync(stoppingToken);
                await sweepService.ReleaseExpiredClaimsAsync(stoppingToken);
                await sweepService.WarnOverdueSlaAsync(stoppingToken);
                await sweepService.WithdrawRoadmapReviewsOfInactiveCreatorsAsync(null, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Moderation sweep iteration failed.");
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
