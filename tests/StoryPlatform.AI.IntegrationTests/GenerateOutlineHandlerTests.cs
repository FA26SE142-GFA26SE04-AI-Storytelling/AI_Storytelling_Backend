using System.Text.Json;
using Microsoft.Extensions.Options;
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
    private const string ValidJson = """{"title":"The Kind Fox","outline":{"opening":"A","development":"B","ending":"C"}}""";

    [Fact]
    public async Task Handle_MapsStructuredLlmOutputAndTraceMetadata()
    {
        var llmClient = new StubLlmClient();
        var result = await CreateHandler(llmClient).HandleAsync(ValidRequest());

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
        var request = ValidRequest() with
        {
            StoryParameters = new StoryParametersDto { Topic = "weapons", Lesson = "safe choices", RequestedLength = 500 },
            Constraints = new GenerationConstraintsDto { MaximumWords = 800, BlockedTopics = ["weapon"] }
        };

        await Assert.ThrowsAsync<ArgumentException>(() => CreateHandler(llmClient).HandleAsync(request));
        Assert.Equal(0, llmClient.CallCount);
    }

    [Fact]
    public async Task Handle_Retries_invalid_json_and_returns_only_valid_outline()
    {
        var llmClient = new StubLlmClient("not-json", ValidJson);
        var response = await CreateHandler(llmClient).HandleAsync(ValidRequest());

        Assert.Equal("The Kind Fox", response.Title);
        Assert.Equal(2, llmClient.CallCount);
        Assert.Equal(2, response.Metadata.AttemptCount);
    }

    [Fact]
    public async Task Handle_Does_not_retry_policy_blocked_output()
    {
        var llmClient = new StubLlmClient(ValidJson.Replace("The Kind Fox", "system prompt"));

        var exception = await Assert.ThrowsAsync<OutlineRejectedException>(() =>
            CreateHandler(llmClient).HandleAsync(ValidRequest()));

        Assert.Equal("OUTLINE_PROMPT_LEAKAGE", exception.ReasonCode);
        Assert.Equal(1, llmClient.CallCount);
    }

    private static GenerateOutlineHandler CreateHandler(StubLlmClient llmClient)
    {
        var options = Options.Create(new OutlineGenerationOptions { BaseRetryDelaySeconds = 0 });
        return new GenerateOutlineHandler(
            llmClient,
            new StubPromptProvider(),
            new RuleBasedOutlineOutputGuardrail(options),
            options);
    }

    private static GenerateOutlineRequest ValidRequest() => new()
    {
        RequestId = "req-1",
        AgeBand = "6-8",
        ReadingLevel = "2",
        VocabularyLevel = "level_2",
        Language = "vi",
        StoryParameters = new StoryParametersDto { Topic = "sharing", Lesson = "kindness", RequestedLength = 500 },
        Constraints = new GenerationConstraintsDto { MaximumWords = 800 }
    };

    private sealed class StubLlmClient(params string[] responses) : ILlmClient
    {
        private readonly Queue<string> _responses = new(responses.Length == 0 ? [ValidJson] : responses);
        public int CallCount { get; private set; }
        public string LastPrompt { get; private set; } = string.Empty;

        public Task<LlmGenerationResult> GenerateStructuredAsync(
            string prompt,
            string schemaName,
            JsonElement schema,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastPrompt = prompt;
            return Task.FromResult(new LlmGenerationResult(
                _responses.Dequeue(), "mock", "mock-model", "1", 10, 20, 5));
        }
    }

    private sealed class StubPromptProvider : IPromptTemplateProvider
    {
        public PromptTemplate GetActive(PromptType promptType, string language, string ageBand) =>
            new("outline-test-v1", "Generate from {{context}}");

        public PromptTemplate GetActiveForOutline(GenerateOutlineRequest request, PromptType promptType = PromptType.Outline)
        {
            // Return a template that includes request data for test assertions
            var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
            var contextJson = JsonSerializer.Serialize(request, jsonOptions);
            return new PromptTemplate("outline-test-v1", $"Generate from context: {contextJson}");
        }
    }
}
