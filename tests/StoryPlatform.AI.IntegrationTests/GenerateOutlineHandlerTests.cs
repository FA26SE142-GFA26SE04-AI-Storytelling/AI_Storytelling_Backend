using System.Text.Json;
using StoryPlatform.AI.Application.Abstractions.LLM;
using StoryPlatform.AI.Application.Abstractions.Prompting;
using StoryPlatform.AI.Application.OutlineGeneration;
using StoryPlatform.AI.Domain.Enums;
using StoryPlatform.AI.Domain.Generation;
using StoryPlatform.Contracts.AI.Models;
using StoryPlatform.Contracts.AI.Requests;
using Xunit;

namespace StoryPlatform.AI.IntegrationTests;

public sealed class GenerateOutlineHandlerTests
{
    [Fact]
    public async Task Handle_MapsStructuredLlmOutputAndTraceMetadata()
    {
        var llmClient = new StubLlmClient();
        var handler = new GenerateOutlineHandler(llmClient, new StubPromptProvider());
        var request = new GenerateOutlineRequest
        {
            RequestId = "req-1",
            AgeBand = "6-8",
            StoryParameters = new StoryParametersDto { Topic = "sharing", RequestedLength = 500 },
            Constraints = new GenerationConstraintsDto { MaximumWords = 800 }
        };

        var result = await handler.HandleAsync(request);

        Assert.Equal("req-1", result.RequestId);
        Assert.Equal("The Kind Fox", result.Title);
        Assert.Equal("outline-test-v1", result.Metadata.PromptVersion);
        Assert.False(string.IsNullOrWhiteSpace(result.GenerationId));
        Assert.Contains("sharing", llmClient.LastPrompt);
        Assert.DoesNotContain("{{context}}", llmClient.LastPrompt);
    }

    [Fact]
    public async Task Handle_BlocksTopicBeforeCallingLlm()
    {
        var llmClient = new StubLlmClient();
        var handler = new GenerateOutlineHandler(llmClient, new StubPromptProvider());
        var request = new GenerateOutlineRequest
        {
            RequestId = "req-2",
            StoryParameters = new StoryParametersDto { Topic = "weapons", RequestedLength = 500 },
            Constraints = new GenerationConstraintsDto { MaximumWords = 800, BlockedTopics = ["weapon"] }
        };

        await Assert.ThrowsAsync<ArgumentException>(() => handler.HandleAsync(request));
        Assert.Equal(0, llmClient.CallCount);
    }

    private sealed class StubLlmClient : ILlmClient
    {
        public int CallCount { get; private set; }
        public string LastPrompt { get; private set; } = string.Empty;

        public Task<LlmGenerationResult> GenerateStructuredAsync(string prompt, string schemaName, JsonElement schema, CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastPrompt = prompt;
            return Task.FromResult(new LlmGenerationResult(
                """{"title":"The Kind Fox","outline":{"opening":"A","development":"B","ending":"C"}}""",
                "mock", "mock-model", "1", 10, 20, 5));
        }
    }

    private sealed class StubPromptProvider : IPromptTemplateProvider
    {
        public PromptTemplate GetActive(PromptType promptType, string language, string ageBand) =>
            new("outline-test-v1", "Generate from {{context}}");
    }
}
