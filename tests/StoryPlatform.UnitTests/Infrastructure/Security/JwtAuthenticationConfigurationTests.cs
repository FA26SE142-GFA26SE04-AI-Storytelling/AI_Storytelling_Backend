using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StoryPlatform.Api.Extensions;
using Xunit;

namespace StoryPlatform.UnitTests.Infrastructure.Security;

public class JwtAuthenticationConfigurationTests
{
    private const string ValidTestSecretKey = "UnitTestJwtSecretKeyValue1234567890ABCDE";

    [Fact]
    public void AddJwtAuthentication_MissingJwtSettingsSection_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        Assert.Throws<InvalidOperationException>(() => services.AddJwtAuthentication(configuration));
    }

    [Fact]
    public void AddJwtAuthentication_SecretKeyShorterThan32Chars_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:SecretKey"] = "too-short-key"
            })
            .Build();

        Assert.Throws<InvalidOperationException>(() => services.AddJwtAuthentication(configuration));
    }

    [Fact]
    public void AddJwtAuthentication_MissingIssuer_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:SecretKey"] = ValidTestSecretKey
            })
            .Build();

        Assert.Throws<InvalidOperationException>(() => services.AddJwtAuthentication(configuration));
    }

    [Fact]
    public void AddJwtAuthentication_ValidSecretKey_RegistersJwtBearerOptionsSuccessfully()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var configuration = BuildValidConfiguration();

        services.AddJwtAuthentication(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
        Assert.NotNull(options.TokenValidationParameters.IssuerSigningKey);
    }

    internal static IConfiguration BuildValidConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:SecretKey"] = ValidTestSecretKey,
                ["JwtSettings:Issuer"] = "StoryPlatform",
                ["JwtSettings:Audience"] = "StoryPlatformClient",
                ["JwtSettings:ExpiryMinutes"] = "120",
                ["JwtSettings:RefreshTokenExpiryDays"] = "7",
                ["JwtSettings:ChildTokenExpiryMinutes"] = "240"
            })
            .Build();
}
