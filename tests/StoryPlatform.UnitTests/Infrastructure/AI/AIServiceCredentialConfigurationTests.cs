using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StoryPlatform.Infrastructure.AI;
using Xunit;

namespace StoryPlatform.UnitTests.Infrastructure.AI;

[CollectionDefinition("AI credential environment", DisableParallelization = true)]
public sealed class AICredentialEnvironmentCollection;

[Collection("AI credential environment")]
public sealed class AIServiceCredentialConfigurationTests
{
    private const string Credential = "test-only-shared-credential";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_primary_key_does_not_mask_shared_configuration(string? primary)
    {
        Assert.Equal(Credential, Resolve(new()
        {
            ["AIService:InternalApiKey"] = primary, ["AI:InternalApiKey"] = " " + Credential + " "
        }));
    }

    [Fact]
    public void Explicit_Core_credential_preserves_precedence_and_is_normalized()
    {
        Assert.Equal(Credential, Resolve(new()
        {
            ["AIService:InternalApiKey"] = " " + Credential + " ", ["AI:InternalApiKey"] = "other-test-only-value"
        }));
    }

    [Fact]
    public void Blank_configuration_uses_plain_environment_key()
    {
        var previous = Environment.GetEnvironmentVariable("AI_INTERNAL_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("AI_INTERNAL_API_KEY", " " + Credential + " ");
            Assert.Equal(Credential, Resolve(new()
            {
                ["AIService:InternalApiKey"] = "", ["AI:InternalApiKey"] = " "
            }));
        }
        finally { Environment.SetEnvironmentVariable("AI_INTERNAL_API_KEY", previous); }
    }

    private static string Resolve(Dictionary<string, string?> values)
    {
        values["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=unused_test;Username=unused";
        var services = new ServiceCollection();
        services.AddLogging();
        StoryPlatform.Infrastructure.DependencyInjection.AddInfrastructure(services,
            new ConfigurationBuilder().AddInMemoryCollection(values).Build());
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<AIServiceOptions>>().Value.InternalApiKey;
    }
}
