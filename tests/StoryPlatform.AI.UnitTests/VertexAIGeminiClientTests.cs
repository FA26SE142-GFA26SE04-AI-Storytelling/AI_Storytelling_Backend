using Microsoft.Extensions.Options;
using StoryPlatform.AI.Infrastructure.LLM.VertexAI;
using Xunit;

namespace StoryPlatform.AI.UnitTests;

public sealed class VertexAIGeminiClientTests
{
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

        var exception = Assert.Throws<InvalidOperationException>(() => new VertexAIGeminiClient(options));
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

        var exception = Assert.Throws<InvalidOperationException>(() => new VertexAIGeminiClient(options));
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

        var exception = Assert.Throws<InvalidOperationException>(() => new VertexAIGeminiClient(options));
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

        var exception = Assert.Throws<InvalidOperationException>(() => new VertexAIGeminiClient(options));
        Assert.Contains("AuthMode", exception.Message);
    }

    [Fact]
    public void VertexAIOptions_DefaultsAreStable()
    {
        var options = new VertexAIOptions();

        Assert.Equal("Adc", options.AuthMode);
        Assert.Equal("gen-lang-client-0675088605", options.ProjectId);
        Assert.Equal("global", options.Location);
        Assert.Equal("gemini-3.8-flash", options.Model);
        Assert.Equal(120, options.TimeoutSeconds);
        Assert.Equal("AI:Google", VertexAIOptions.SectionName);
    }
}
