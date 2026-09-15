using System.Text.Json;
using Microsoft.Extensions.Options;
using StoryPlatform.AI.Application.Abstractions.LLM;
using StoryPlatform.AI.Application.Abstractions.Prompting;
using StoryPlatform.AI.Application.ContentGeneration;
using StoryPlatform.AI.Domain.Enums;
using StoryPlatform.AI.Domain.Generation;
using StoryPlatform.Contracts.AI.Models;
using StoryPlatform.Contracts.AI.Requests;
using Xunit;

namespace StoryPlatform.AI.IntegrationTests;

public sealed class StoryContentGenerationHandlerTests
{
    [Fact]
    public async Task Content_handler_retries_invalid_payload_and_returns_content_only()
    {
        var llm = new StubLlmClient("not-json", """{"title":"Tình bạn","storySections":[{"order":1,"heading":"Mở đầu","content":"Lan chia sẻ sách với Minh."}],"lesson":"Biết chia sẻ"}""");
        var options = Options.Create(new StoryContentGenerationOptions { BaseRetryDelaySeconds = 0 });
        var handler = new GenerateStoryContentHandler(new StoryContentGenerationExecutor(llm, options), new StubPromptProvider());

        var response = await handler.HandleAsync(new GenerateStoryContentRequest
        {
            RequestId = "content-1", AgeBand = "6-8", ReadingLevel = "2", VocabularyLevel = "level_2", Language = "vi",
            ApprovedOutlineReference = "version-1", Outline = new StoryOutlineDto("Lan gặp Minh", "Hai bạn đọc", "Hai bạn chia sẻ"),
            StoryParameters = new StoryParametersDto { Topic = "Tình bạn", Lesson = "Biết chia sẻ", RequestedLength = 500 },
            Constraints = new GenerationConstraintsDto { MaximumWords = 700 }
        });

        Assert.Equal(2, llm.CallCount);
        Assert.Equal(2, response.Metadata.AttemptCount);
        Assert.Equal("Tình bạn", response.Story.Title);
        Assert.Single(response.Story.StorySections);
    }

    private sealed class StubLlmClient(params string[] responses) : ILlmClient
    {
        private readonly Queue<string> _responses = new(responses);
        public int CallCount { get; private set; }
        public Task<LlmGenerationResult> GenerateStructuredAsync(string prompt, string schemaName, JsonElement schema, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new LlmGenerationResult(_responses.Dequeue(), "test", "test", "1", 1, 1, 1));
        }
    }

    private sealed class StubPromptProvider : IPromptTemplateProvider
    {
        public PromptTemplate GetActive(PromptType promptType, string language, string ageBand) =>
            new("story-content-test-v1", "Generate {{context}}");

        public PromptTemplate GetActiveForOutline(
            GenerateOutlineRequest request,
            PromptType promptType = PromptType.Outline) =>
            GetActive(promptType, request.Language, request.AgeBand);
    }
}
