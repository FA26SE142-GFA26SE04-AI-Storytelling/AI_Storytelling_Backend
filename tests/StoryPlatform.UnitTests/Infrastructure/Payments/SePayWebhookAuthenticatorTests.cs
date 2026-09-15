using Microsoft.Extensions.Options;
using StoryPlatform.Infrastructure.Payments;
using Xunit;

namespace StoryPlatform.UnitTests.Infrastructure.Payments;

public class SePayWebhookAuthenticatorTests
{
    private static SePayWebhookAuthenticator MakeAuthenticator(string configuredKey) =>
        new(Options.Create(new SePayOptions { WebhookApiKey = configuredKey }));

    [Fact]
    public void IsValid_MatchingApiKeyHeader_ReturnsTrue()
    {
        var authenticator = MakeAuthenticator("secret-123");

        Assert.True(authenticator.IsValid("Apikey secret-123"));
    }

    [Fact]
    public void IsValid_WrongApiKey_ReturnsFalse()
    {
        var authenticator = MakeAuthenticator("secret-123");

        Assert.False(authenticator.IsValid("Apikey wrong-key"));
    }

    [Fact]
    public void IsValid_MissingHeader_ReturnsFalse()
    {
        var authenticator = MakeAuthenticator("secret-123");

        Assert.False(authenticator.IsValid(null));
    }

    [Fact]
    public void IsValid_ConfiguredKeyEmpty_ReturnsFalse()
    {
        var authenticator = MakeAuthenticator(string.Empty);

        Assert.False(authenticator.IsValid("Apikey secret-123"));
    }

    [Fact]
    public void IsValid_HeaderWithoutApikeyPrefix_ReturnsFalse()
    {
        var authenticator = MakeAuthenticator("secret-123");

        Assert.False(authenticator.IsValid("secret-123"));
    }
}
