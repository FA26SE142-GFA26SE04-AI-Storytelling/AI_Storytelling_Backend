using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StoryPlatform.Api.Extensions;
using Xunit;

namespace StoryPlatform.UnitTests.Infrastructure.Security;

/// <summary>DF06: CORS origins and rate-limit settings are read and validated from configuration.</summary>
public class SecurityConfigurationTests
{
    [Fact]
    public void ResolveCorsOrigins_ConfiguredList_IsTrimmedDeduplicatedAndStripsTrailingSlash()
    {
        var origins = ServiceExtensions.ResolveCorsOrigins(Config(
            ("Cors:AllowedOrigins:0", " https://www.taletale.site/ "),
            ("Cors:AllowedOrigins:1", "https://WWW.taletale.site"),
            ("Cors:AllowedOrigins:2", "http://localhost:3000")), isDevelopment: false);

        Assert.Equal(["https://www.taletale.site", "http://localhost:3000"], origins);
    }

    [Fact]
    public void ResolveCorsOrigins_NothingConfiguredInDevelopment_FallsBackToLocalWeb()
    {
        var origins = ServiceExtensions.ResolveCorsOrigins(Config(), isDevelopment: true);

        Assert.Equal(["http://localhost:3000", "http://127.0.0.1:3000"], origins);
    }

    [Fact]
    public void ResolveCorsOrigins_NothingConfiguredOutsideDevelopment_AllowsNoOrigin()
    {
        Assert.Empty(ServiceExtensions.ResolveCorsOrigins(Config(), isDevelopment: false));
    }

    [Theory]
    [InlineData("*")]
    [InlineData("www.taletale.site")]
    [InlineData("https://www.taletale.site/app")]
    [InlineData("https://www.taletale.site?x=1")]
    [InlineData("https://www.taletale.site/?x=1")]
    [InlineData("https://www.taletale.site?")]
    [InlineData("https://www.taletale.site#top")]
    [InlineData("https://www.taletale.site#")]
    [InlineData("https://admin@www.taletale.site")]
    [InlineData("https://@www.taletale.site")]
    public void ResolveCorsOrigins_WildcardOrNonOriginValue_ThrowsInvalidOperation(string origin)
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            ServiceExtensions.ResolveCorsOrigins(Config(("Cors:AllowedOrigins:0", origin)), isDevelopment: false));

        Assert.Equal($"'Cors:AllowedOrigins' chỉ nhận origin dạng scheme://host[:port], không nhận '{origin}'.", error.Message);
    }

    [Fact]
    public void AddCorsPolicy_RegistersNamedPolicyWithoutAnyOriginOrCredentials()
    {
        var services = new ServiceCollection();
        services.AddCorsPolicy(Config(("Cors:AllowedOrigins:0", "https://www.taletale.site")), isDevelopment: false);

        var policy = services.BuildServiceProvider().GetRequiredService<IOptions<CorsOptions>>().Value
            .GetPolicy(ServiceExtensions.CorsPolicyName)!;

        Assert.Equal(["https://www.taletale.site"], policy.Origins);
        Assert.False(policy.AllowAnyOrigin);
        Assert.False(policy.SupportsCredentials);
    }

    [Theory]
    [InlineData("RateLimiting:Auth:PermitLimit", "0")]
    [InlineData("RateLimiting:Ai:WindowSeconds", "-5")]
    public void AddApiRateLimiting_NonPositiveSetting_ThrowsInvalidOperation(string key, string value)
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddApiRateLimiting(Config((key, value))));

        Assert.Contains("phải là số nguyên dương", error.Message);
    }

    [Fact]
    public void AddApiRateLimiting_DefaultSettings_RegistersLimiter()
    {
        var services = new ServiceCollection();
        services.AddApiRateLimiting(Config());

        Assert.NotNull(services.BuildServiceProvider().GetService<IOptions<Microsoft.AspNetCore.RateLimiting.RateLimiterOptions>>());
    }

    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(item => new KeyValuePair<string, string?>(item.Key, item.Value)))
            .Build();
}
