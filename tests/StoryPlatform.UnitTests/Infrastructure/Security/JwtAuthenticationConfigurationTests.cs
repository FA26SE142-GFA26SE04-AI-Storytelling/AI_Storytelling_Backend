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

    [Theory]
    [InlineData("appsettings.json")]
    [InlineData("appsettings.Development.json")]
    public void ApiAppSettings_UseAgreedTokenLifetimes(string fileName)
    {
        var settingsPath = FindApiSettingsFile(fileName);
        if (!File.Exists(settingsPath))
        {
            // Local appsettings files intentionally stay gitignored because they may contain secrets.
            return;
        }

        var configuration = new ConfigurationBuilder()
            .AddJsonFile(settingsPath, optional: false)
            .Build();

        Assert.Equal("15", configuration["JwtSettings:ExpiryMinutes"]);
        Assert.Equal("7", configuration["JwtSettings:RefreshTokenExpiryDays"]);
        Assert.Equal("240", configuration["JwtSettings:ChildTokenExpiryMinutes"]);
    }

    [Fact]
    public void TrackedDeploymentConfiguration_UsesAgreedTokenLifetimes()
    {
        var root = FindRepositoryRoot();
        var composeLines = File.ReadLines(Path.Combine(root, "compose.yaml"));
        var cdkLines = File.ReadLines(Path.Combine(
            root, "infra", "aws-cdk", "src", "StoryPlatformCoreStack.cs"));

        Assert.Contains(composeLines, line => line.Contains(
            "JwtSettings__ExpiryMinutes: ${JWT_EXPIRY_MINUTES:-15}", StringComparison.Ordinal));
        Assert.Contains(composeLines, line => line.Contains(
            "JwtSettings__ChildTokenExpiryMinutes: ${JWT_CHILD_TOKEN_EXPIRY_MINUTES:-240}",
            StringComparison.Ordinal));
        Assert.Contains(cdkLines, line => line.Contains(
            "[\"JwtSettings__ExpiryMinutes\"] = \"15\"", StringComparison.Ordinal));
        Assert.Contains(cdkLines, line => line.Contains(
            "[\"JwtSettings__ChildTokenExpiryMinutes\"] = \"240\"", StringComparison.Ordinal));
    }

    private static string FindApiSettingsFile(string fileName)
        => Path.Combine(FindRepositoryRoot(), "src", "Core", "StoryPlatform.Api", fileName);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "StoryPlatform.sln")))
        {
            directory = directory.Parent;
        }

        if (directory == null)
        {
            throw new InvalidOperationException("Không tìm thấy thư mục gốc chứa StoryPlatform.sln.");
        }

        return directory.FullName;
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
