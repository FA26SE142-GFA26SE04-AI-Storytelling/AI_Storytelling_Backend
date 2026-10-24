using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StoryPlatform.Application.Features.StoryWishes.Interfaces;

namespace StoryPlatform.Infrastructure.BackgroundServices;

public sealed class StoryWishSweepWorkerOptions
{
    public const string SectionName = "StoryWishes:SweepWorker";
    public int IntervalMinutes { get; set; } = 15;
}

/// <summary>
/// Job định kỳ cho điều ước: dọn bản nháp 24 giờ, nhắc ngày 6, hết hạn ngày 7, tự "đã thành truyện" khi truyện mới
/// sẵn sàng, hủy khi hồ sơ trẻ ngừng hoạt động.
/// </summary>
public sealed class StoryWishSweepWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StoryWishSweepWorker> _logger;
    private readonly StoryWishSweepWorkerOptions _options;

    public StoryWishSweepWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<StoryWishSweepWorker> logger,
        IOptions<StoryWishSweepWorkerOptions> options)
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
                var result = await scope.ServiceProvider.GetRequiredService<IStoryWishSweepService>()
                    .SweepAsync(stoppingToken);
                _logger.LogInformation("Story wish sweep: {Result}", result);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Story wish sweep iteration failed.");
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
