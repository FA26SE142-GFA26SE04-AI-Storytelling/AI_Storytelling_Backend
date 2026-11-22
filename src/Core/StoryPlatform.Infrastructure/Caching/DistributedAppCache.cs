using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using StoryPlatform.Application.Abstractions.Caching;

namespace StoryPlatform.Infrastructure.Caching;

/// <summary>
/// <see cref="IAppCache"/> trên <see cref="IDistributedCache"/> (Redis). Mọi lỗi đọc/ghi/xoá cache chỉ ghi log
/// cảnh báo rồi coi như cache miss, để Redis không bao giờ là phụ thuộc bắt buộc (spec §5.8).
/// </summary>
public sealed class DistributedAppCache : IAppCache
{
    private const string KeyPrefix = "storyplatform:";

    private readonly IDistributedCache _cache;
    private readonly ILogger<DistributedAppCache> _logger;

    public DistributedAppCache(IDistributedCache cache, ILogger<DistributedAppCache> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public async Task<T> GetOrSetAsync<T>(string key, Func<CancellationToken, Task<T>> factory, TimeSpan ttl,
        CancellationToken cancellationToken = default)
    {
        var fullKey = KeyPrefix + key;
        try
        {
            var cached = await _cache.GetStringAsync(fullKey, cancellationToken);
            if (cached is not null)
            {
                return JsonSerializer.Deserialize<T>(cached)!;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Cache read failed for {CacheKey}; falling back to source.", fullKey);
        }

        var value = await factory(cancellationToken);
        try
        {
            await _cache.SetStringAsync(fullKey, JsonSerializer.Serialize(value),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl }, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Cache write failed for {CacheKey}.", fullKey);
        }

        return value;
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            await _cache.RemoveAsync(KeyPrefix + key, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Cache remove failed for {CacheKey}.", KeyPrefix + key);
        }
    }
}
