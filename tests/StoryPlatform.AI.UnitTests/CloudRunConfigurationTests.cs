using Microsoft.Extensions.Configuration;
using StoryPlatform.AI.Api.Configuration;
using Xunit;

namespace StoryPlatform.AI.UnitTests;

public sealed class CloudRunConfigurationTests
{
    [Fact]
    public void Local_configuration_and_ADC_are_unchanged()
    {
        CloudRunConfiguration.Validate(Config(new() { ["GOOGLE_APPLICATION_CREDENTIALS"] = "local-adc.json" }));
    }

    [Fact]
    public void Runtime_identity_configuration_is_accepted() => CloudRunConfiguration.Validate(Config(Valid()));

    [Theory]
    [InlineData("AI:Google:ProjectId")]
    [InlineData("AI:Google:Location")]
    [InlineData("AI:Google:Model")]
    [InlineData("AI:InternalApiKey")]
    public void Cloud_Run_rejects_missing_explicit_configuration(string key)
    {
        var values = Valid();
        values[key] = " ";
        Assert.Contains(key, Assert.Throws<InvalidOperationException>(() => CloudRunConfiguration.Validate(Config(values))).Message);
    }

    [Theory]
    [InlineData("AI:Provider", "Gemini")]
    [InlineData("AI:Google:AuthMode", "ApiKey")]
    [InlineData("GOOGLE_APPLICATION_CREDENTIALS", "local-adc.json")]
    [InlineData("AI:Google:TimeoutSeconds", "0")]
    [InlineData("AI:Google:TimeoutSeconds", "301")]
    public void Cloud_Run_rejects_wrong_identity_or_deadline(string key, string value)
    {
        var values = Valid();
        values[key] = value;
        Assert.Throws<InvalidOperationException>(() => CloudRunConfiguration.Validate(Config(values)));
    }

    private static IConfiguration Config(Dictionary<string, string?> values) => new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    private static Dictionary<string, string?> Valid() => new()
    {
        ["K_SERVICE"] = "ai-test", ["AI:Provider"] = "Vertex", ["AI:Google:AuthMode"] = "Adc",
        ["AI:Google:ProjectId"] = "test-project", ["AI:Google:Location"] = "global",
        ["AI:Google:Model"] = "test-model", ["AI:InternalApiKey"] = "test-only-key", ["AI:Google:TimeoutSeconds"] = "120"
    };
}
