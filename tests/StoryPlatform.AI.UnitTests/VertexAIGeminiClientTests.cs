using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StoryPlatform.AI.Infrastructure.LLM.VertexAI;
using Xunit;

namespace StoryPlatform.AI.UnitTests;

public sealed class VertexAIGeminiClientTests
{
    private static ILogger<VertexAIGeminiClient> CreateNullLogger() =>
        NullLogger<VertexAIGeminiClient>.Instance;

    [Theory]
    [InlineData("", "us-central1", "gemini-1.5-flash-002")]
    [InlineData("  ", "us-central1", "gemini-1.5-flash-002")]
    public void Constructor_MissingProjectId_Throws(string projectId, string location, string model)
    {
        var options = Options.Create(new VertexAIOptions
        {
            AuthMode = "Adc",
            ProjectId = projectId,
            Location = location,
            Model = model
        });

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new VertexAIGeminiClient(options, CreateNullLogger()));
        Assert.Contains("ProjectId", exception.Message);
    }

    [Theory]
    [InlineData("gen-lang-client-0675088605", "", "gemini-1.5-flash-002")]
    [InlineData("gen-lang-client-0675088605", "  ", "gemini-1.5-flash-002")]
    public void Constructor_MissingLocation_Throws(string projectId, string location, string model)
    {
        var options = Options.Create(new VertexAIOptions
        {
            AuthMode = "Adc",
            ProjectId = projectId,
            Location = location,
            Model = model
        });

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new VertexAIGeminiClient(options, CreateNullLogger()));
        Assert.Contains("Location", exception.Message);
    }

    [Theory]
    [InlineData("gen-lang-client-0675088605", "us-central1", "")]
    [InlineData("gen-lang-client-0675088605", "us-central1", "  ")]
    public void Constructor_MissingModel_Throws(string projectId, string location, string model)
    {
        var options = Options.Create(new VertexAIOptions
        {
            AuthMode = "Adc",
            ProjectId = projectId,
            Location = location,
            Model = model
        });

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new VertexAIGeminiClient(options, CreateNullLogger()));
        Assert.Contains("Model", exception.Message);
    }

    [Fact]
    public void Constructor_RejectsNonAdcAuthMode()
    {
        var options = Options.Create(new VertexAIOptions
        {
            AuthMode = "ApiKey",
            ProjectId = "gen-lang-client-0675088605",
            Location = "us-central1",
            Model = "gemini-1.5-flash-002"
        });

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new VertexAIGeminiClient(options, CreateNullLogger()));
        Assert.Contains("AuthMode", exception.Message);
    }

    [Fact]
    public void VertexAIOptions_DefaultsAreStable()
    {
        var options = new VertexAIOptions();

        Assert.Equal("Adc", options.AuthMode);
        Assert.Equal("gen-lang-client-0675088605", options.ProjectId);
        Assert.Equal("us-central1", options.Location);
        Assert.Equal("gemini-1.5-flash-002", options.Model);
        Assert.Equal(120, options.TimeoutSeconds);
        Assert.Equal("AI:Google", VertexAIOptions.SectionName);
    }
}
