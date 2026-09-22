using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StoryPlatform.Infrastructure.Caching;
using Xunit;

namespace StoryPlatform.UnitTests.Infrastructure.Caching;

public class RedisCacheExtensionsTests
{
    private static IConfiguration BuildConfiguration(string connectionString) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new System.Collections.Generic.Dictionary<string, string?>
            {
                ["RedisSettings:ConnectionString"] = connectionString
            })
            .Build();

    [Fact]
    public void AddRedisCache_RegistersIDistributedCache()
    {
        var services = new ServiceCollection();

        services.AddRedisCache(BuildConfiguration("localhost:6379"));
        var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<IDistributedCache>());
    }

    [Fact]
    public void BuildRedisConfiguration_DisablesAbortOnConnectFail()
    {
        var options = RedisCacheExtensions.BuildRedisConfiguration("localhost:6379");

        Assert.False(options.AbortOnConnectFail);
    }

    [Fact]
    public void BuildRedisConfiguration_UsesShortConnectTimeout()
    {
        var options = RedisCacheExtensions.BuildRedisConfiguration("localhost:6379");

        Assert.True(options.ConnectTimeout <= 2000);
    }

    [Fact]
    public void BuildRedisConfiguration_RetriesConnectionAtMostOnce()
    {
        var options = RedisCacheExtensions.BuildRedisConfiguration("localhost:6379");

        Assert.Equal(1, options.ConnectRetry);
    }
}
