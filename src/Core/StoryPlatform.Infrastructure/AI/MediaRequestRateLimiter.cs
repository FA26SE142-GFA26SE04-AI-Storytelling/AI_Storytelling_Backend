using StoryPlatform.Application.Features.MediaGeneration;

namespace StoryPlatform.Infrastructure.AI;

internal sealed class MediaRequestRateLimiter
{
    private readonly object _sync = new();
    private readonly int _capacity;
    private readonly double _tokensPerSecond;
    private double _tokens;
    private DateTime _lastRefill = DateTime.UtcNow;

    public MediaRequestRateLimiter(MediaGenerationOptions options)
    {
        _capacity = Math.Max(1, options.RateLimit.BurstSize);
        _tokensPerSecond = Math.Max(1, options.RateLimit.RequestsPerMinute) / 60d;
        _tokens = _capacity;
    }

    public async ValueTask WaitAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            TimeSpan wait;
            lock (_sync)
            {
                var now = DateTime.UtcNow;
                _tokens = Math.Min(_capacity, _tokens + (now - _lastRefill).TotalSeconds * _tokensPerSecond);
                _lastRefill = now;
                if (_tokens >= 1d)
                {
                    _tokens -= 1d;
                    return;
                }

                wait = TimeSpan.FromSeconds((1d - _tokens) / _tokensPerSecond);
            }

            await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
        }
    }
}

internal sealed class MediaRateLimitDelegatingHandler(MediaRequestRateLimiter limiter) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await limiter.WaitAsync(cancellationToken).ConfigureAwait(false);
        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
