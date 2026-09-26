using System.Text.Json;
using System.Net;
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
        Assert.Equal("Lan chia sẻ sách với Minh.", response.Story.Description);
    }

    [Fact]
    public async Task Content_handler_retries_vertex_rate_limit_and_then_succeeds()
    {
        var llm = new StubLlmClient(
            new HttpRequestException("Resource exhausted.", null, HttpStatusCode.TooManyRequests),
            """{"title":"Tình bạn","storySections":[{"order":1,"heading":"Mở đầu","content":"Lan chia sẻ sách với Minh."}],"lesson":"Biết chia sẻ"}""");
        var options = Options.Create(new StoryContentGenerationOptions { BaseRetryDelaySeconds = 0 });
        var handler = new GenerateStoryContentHandler(new StoryContentGenerationExecutor(llm, options), new StubPromptProvider());

        var response = await handler.HandleAsync(ValidRequest());

        Assert.Equal(2, llm.CallCount);
        Assert.Equal(2, response.Metadata.AttemptCount);
    }

    [Fact]
    public async Task Content_handler_reports_retry_exhaustion_with_last_provider_error()
    {
        var errors = Enumerable.Range(0, 3)
            .Select(_ => (object)new HttpRequestException("Resource exhausted.", null, HttpStatusCode.TooManyRequests))
            .ToArray();
        var llm = new StubLlmClient(errors);
        var options = Options.Create(new StoryContentGenerationOptions
        {
            BaseRetryDelaySeconds = 0,
            MaxTechnicalAttempts = 3
        });
        var handler = new GenerateStoryContentHandler(new StoryContentGenerationExecutor(llm, options), new StubPromptProvider());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(ValidRequest()));

        Assert.Equal(3, llm.CallCount);
        Assert.Contains("configured attempts", exception.Message);
        var providerError = Assert.IsType<HttpRequestException>(exception.InnerException);
        Assert.Equal(HttpStatusCode.TooManyRequests, providerError.StatusCode);
    }

    [Fact]
    public async Task Refine_handler_accepts_imported_story_without_lesson_and_returns_complete_story()
    {
        var llm = new StubLlmClient(
            """{"title":"Tình bạn","description":"Một câu chuyện ấm áp về hai người bạn cùng chia sẻ sách.","storySections":[{"order":1,"heading":"Câu chuyện","content":"Lan chia sẻ sách với Minh."}],"lesson":"Biết chia sẻ"}""");
        var options = Options.Create(new StoryContentGenerationOptions { BaseRetryDelaySeconds = 0 });
        var handler = new RefineStoryContentHandler(
            new StoryContentGenerationExecutor(llm, options),
            new StubPromptProvider());

        var response = await handler.HandleAsync(new RefineStoryContentRequest
        {
            RequestId = "adapt-21-v1",
            Story = new StoryContentDto
            {
                Title = "Tình bạn",
                Lesson = string.Empty,
                StorySections = [new StorySectionDto(1, string.Empty, "Lan có một quyển sách.")]
            },
            Language = "vi",
            AgeBand = "6-8",
            Reasons = ["Viết lại bằng câu ngắn gọn cho bé 6 tuổi."]
        });

        Assert.Equal(1, llm.CallCount);
        Assert.Equal("Biết chia sẻ", response.Story.Lesson);
        Assert.Equal("Một câu chuyện ấm áp về hai người bạn cùng chia sẻ sách.", response.Story.Description);
        Assert.Single(response.Story.StorySections);
    }

    [Fact]
    public async Task Refine_handler_rejects_invalid_input_as_bad_request_before_calling_llm()
    {
        var llm = new StubLlmClient(
            """{"title":"Tình bạn","storySections":[{"order":1,"heading":"Câu chuyện","content":"Lan chia sẻ sách."}],"lesson":"Biết chia sẻ"}""");
        var options = Options.Create(new StoryContentGenerationOptions { BaseRetryDelaySeconds = 0 });
        var handler = new RefineStoryContentHandler(
            new StoryContentGenerationExecutor(llm, options),
            new StubPromptProvider());

        await Assert.ThrowsAsync<ArgumentException>(() => handler.HandleAsync(new RefineStoryContentRequest
        {
            RequestId = "adapt-invalid",
            Story = new StoryContentDto
            {
                Title = string.Empty,
                StorySections = [new StorySectionDto(1, string.Empty, "Lan có một quyển sách.")]
            },
            Reasons = ["Viết lại."]
        }));

        Assert.Equal(0, llm.CallCount);
    }

    private static GenerateStoryContentRequest ValidRequest() => new()
    {
        RequestId = "content-1", AgeBand = "6-8", ReadingLevel = "2", VocabularyLevel = "level_2", Language = "vi",
        ApprovedOutlineReference = "version-1", Outline = new StoryOutlineDto("Lan gặp Minh", "Hai bạn đọc", "Hai bạn chia sẻ"),
        StoryParameters = new StoryParametersDto { Topic = "Tình bạn", Lesson = "Biết chia sẻ", RequestedLength = 500 },
        Constraints = new GenerationConstraintsDto { MaximumWords = 700 }
    };

    private sealed class StubLlmClient(params object[] responses) : ILlmClient
    {
        private readonly Queue<object> _responses = new(responses);
        public int CallCount { get; private set; }
        public Task<LlmGenerationResult> GenerateStructuredAsync(string prompt, string schemaName, JsonElement schema, CancellationToken cancellationToken = default)
        {
            CallCount++;
            var response = _responses.Dequeue();
            if (response is Exception exception)
            {
                return Task.FromException<LlmGenerationResult>(exception);
            }

            return Task.FromResult(new LlmGenerationResult((string)response, "test", "test", "1", 1, 1, 1));
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
