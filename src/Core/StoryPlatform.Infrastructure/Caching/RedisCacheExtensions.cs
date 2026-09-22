using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace StoryPlatform.Infrastructure.Caching;

public static class RedisCacheExtensions
{
    public static IServiceCollection AddRedisCache(this IServiceCollection services, IConfiguration configuration)
    {
        var redisOptions = new RedisOptions();
        configuration.GetSection(RedisOptions.SectionName).Bind(redisOptions);
        services.AddSingleton(redisOptions);

        services.AddStackExchangeRedisCache(options =>
        {
            options.ConfigurationOptions = BuildRedisConfiguration(redisOptions.ConnectionString);
        });

        return services;
    }

    /// <summary>
    /// Redis is an external, best-effort cache provider (spec §5.8) — never a hard dependency
    /// for correctness. These settings make any future IDistributedCache call fail fast instead
    /// of hanging or crashing the app when the provider is briefly unreachable (network blip,
    /// provider-side maintenance), so callers can safely treat a failure as a cache miss.
    /// </summary>
    public static ConfigurationOptions BuildRedisConfiguration(string connectionString)
    {
        var configurationOptions = string.IsNullOrWhiteSpace(connectionString)
            ? new ConfigurationOptions()
            : ConfigurationOptions.Parse(connectionString);

        configurationOptions.AbortOnConnectFail = false;
        configurationOptions.ConnectTimeout = 2000;
        configurationOptions.SyncTimeout = 1000;
        configurationOptions.ConnectRetry = 1;

        return configurationOptions;
    }
}
