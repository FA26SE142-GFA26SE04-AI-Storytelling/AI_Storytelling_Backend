using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StoryPlatform.Application.Abstractions.Caching;
using StoryPlatform.Infrastructure.Caching;
using Xunit;

namespace StoryPlatform.UnitTests.Infrastructure.Caching;

public class DistributedAppCacheTests
{
    private sealed record Sample(int Id, string Name);

    private sealed class DictionaryDistributedCache : IDistributedCache
    {
        public readonly ConcurrentDictionary<string, byte[]> Store = new();
        public byte[]? Get(string key) => Store.TryGetValue(key, out var value) ? value : null;
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult(Get(key));
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => Store[key] = value;
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            Set(key, value, options);
            return Task.CompletedTask;
        }
        public void Refresh(string key) { }
        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
        public void Remove(string key) => Store.TryRemove(key, out _);
        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            Remove(key);
            return Task.CompletedTask;
        }
    }

    private static DistributedAppCache Create(IDistributedCache cache) =>
        new(cache, NullLogger<DistributedAppCache>.Instance);

    [Fact]
    public async Task GetOrSetAsync_SecondCall_ServedFromCacheWithoutCallingFactory()
    {
        var sut = Create(new DictionaryDistributedCache());
        var calls = 0;
        Task<Sample> Factory(CancellationToken _) { calls++; return Task.FromResult(new Sample(1, "Mèo")); }

        var first = await sut.GetOrSetAsync("k", Factory, TimeSpan.FromMinutes(1));
        var second = await sut.GetOrSetAsync("k", Factory, TimeSpan.FromMinutes(1));

        Assert.Equal(1, calls);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task GetOrSetAsync_StoresUnderPrefixedKey()
    {
        var backing = new DictionaryDistributedCache();

        await Create(backing).GetOrSetAsync("k", _ => Task.FromResult(new Sample(1, "a")), TimeSpan.FromMinutes(1));

        Assert.True(backing.Store.ContainsKey("storyplatform:k"));
    }

    [Fact]
    public async Task RemoveAsync_ForcesFactoryOnNextCall()
    {
        var sut = Create(new DictionaryDistributedCache());
        var calls = 0;
        Task<Sample> Factory(CancellationToken _) { calls++; return Task.FromResult(new Sample(calls, "x")); }

        await sut.GetOrSetAsync("k", Factory, TimeSpan.FromMinutes(1));
        await sut.RemoveAsync("k");
        var refreshed = await sut.GetOrSetAsync("k", Factory, TimeSpan.FromMinutes(1));

        Assert.Equal(2, calls);
        Assert.Equal(2, refreshed.Id);
    }

    [Fact]
    public async Task GetOrSetAsync_CacheUnavailable_FallsBackToFactory()
    {
        var broken = new Mock<IDistributedCache>();
        broken.Setup(cache => cache.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("redis down"));
        broken.Setup(cache => cache.SetAsync(It.IsAny<string>(), It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("redis down"));
        broken.Setup(cache => cache.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("redis down"));
        var sut = Create(broken.Object);

        var result = await sut.GetOrSetAsync("k", _ => Task.FromResult(new Sample(7, "db")), TimeSpan.FromMinutes(1));
        await sut.RemoveAsync("k");

        Assert.Equal(new Sample(7, "db"), result);
    }

    [Fact]
    public async Task GetOrSetAsync_FactoryThrows_PropagatesAndCachesNothing()
    {
        var backing = new DictionaryDistributedCache();

        await Assert.ThrowsAsync<KeyNotFoundException>(() => Create(backing).GetOrSetAsync<Sample>(
            "k", _ => throw new KeyNotFoundException(), TimeSpan.FromMinutes(1)));

        Assert.Empty(backing.Store);
    }

    [Fact]
    public async Task NullCache_GetOrSetOrLoad_CallsFactoryEveryTime()
    {
        IAppCache? cache = null;
        var calls = 0;
        Task<int> Factory(CancellationToken _) => Task.FromResult(++calls);

        await cache.GetOrSetOrLoadAsync("k", Factory, TimeSpan.FromMinutes(1));
        await cache.GetOrSetOrLoadAsync("k", Factory, TimeSpan.FromMinutes(1));
        await cache.RemoveIfConfiguredAsync("k");

        Assert.Equal(2, calls);
    }
}

/// <summary>IAppCache trong bộ nhớ cho unit test của service: đếm số lần xoá key để kiểm việc làm mới cache.</summary>
public sealed class InMemoryAppCache : IAppCache
{
    private readonly ConcurrentDictionary<string, object?> _store = new();
    public List<string> RemovedKeys { get; } = new();

    public bool Contains(string key) => _store.ContainsKey(key);

    public async Task<T> GetOrSetAsync<T>(string key, Func<CancellationToken, Task<T>> factory, TimeSpan ttl,
        CancellationToken cancellationToken = default)
    {
        if (_store.TryGetValue(key, out var cached)) return (T)cached!;
        var value = await factory(cancellationToken);
        _store[key] = value;
        return value;
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        RemovedKeys.Add(key);
        _store.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}
