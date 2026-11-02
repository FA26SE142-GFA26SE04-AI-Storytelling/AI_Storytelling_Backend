using System.Text.Json;
using Microsoft.Extensions.Options;
using StoryPlatform.AI.Application.Abstractions.LLM;
using StoryPlatform.AI.Application.Refinement;
using StoryPlatform.AI.Application.StoryGeneration;
using StoryPlatform.AI.Domain.Generation;
using StoryPlatform.AI.Infrastructure.Evaluation;
using StoryPlatform.AI.Infrastructure.PromptCatalog;
using StoryPlatform.Contracts.AI.Models;
using StoryPlatform.Contracts.AI.Requests;
using Xunit;

namespace StoryPlatform.AI.IntegrationTests;

public sealed class StoryPackageWithoutDiscussionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Generate_and_refine_accept_story_packages_without_discussion(bool refine)
    {
        var llm = new CapturingLlm();
        var prompts = new PromptTemplateProvider();
        var evaluation = new RuleBasedStoryEvaluationService();
        var constraints = new GenerationConstraintsDto { MinimumWords = 1, MaximumWords = 100 };
        var original = new StoryPackageDto { AgeBand = "6-8", ReadingLevel = "2", VocabularyLevel = "level_2" };
        StoryPackageDto story;
        EvaluationResultDto result;
        if (refine)
        {
            var response = await new RefineStoryHandler(llm, prompts, evaluation).HandleAsync(new RefineStoryRequest
            {
                RequestId = "refine-no-discussion", Story = original, Constraints = constraints
            });
            story = response.Story;
            result = response.Evaluation;
        }
        else
        {
            var response = await new GenerateStoryHandler(llm, prompts, evaluation,
                Options.Create(new GenerateStoryOptions())).HandleAsync(new GenerateStoryRequest
            {
                RequestId = "generate-no-discussion", AgeBand = "6-8", ReadingLevel = "2", VocabularyLevel = "level_2",
                ApprovedOutlineReference = "outline-1", Outline = new("Lan gặp Minh", "Lan chia sẻ sách", "Minh cảm ơn"),
                StoryParameters = new() { RequestedLength = 50 }, Constraints = constraints
            });
            story = response.Story;
            result = response.Evaluation;
        }

        Assert.True(result.Passed);
        Assert.Empty(story.DiscussionQuestions);
        Assert.Equal(1, llm.Calls);
        Assert.False(llm.Schema.GetProperty("properties").TryGetProperty("discussionQuestions", out _));
        Assert.DoesNotContain(llm.Schema.GetProperty("required").EnumerateArray(), x => x.GetString() == "discussionQuestions");
        Assert.False(llm.Schema.GetProperty("additionalProperties").GetBoolean());
        Assert.Contains("Do not generate discussion questions.", llm.Prompt);
    }

    private sealed class CapturingLlm : ILlmClient
    {
        public int Calls { get; private set; }
        public JsonElement Schema { get; private set; }
        public string Prompt { get; private set; } = string.Empty;

        public Task<LlmGenerationResult> GenerateStructuredAsync(
            string prompt, string schemaName, JsonElement schema, CancellationToken cancellationToken = default)
        {
            Calls++;
            Schema = schema.Clone();
            Prompt = prompt;
            return Task.FromResult(new LlmGenerationResult("""
                {
                  "title":"Chia sẻ sách",
                  "storySections":[{"order":1,"heading":"Tình bạn","content":"Lan chia sẻ sách. Minh cảm ơn bạn."}],
                  "lesson":"Biết chia sẻ",
                  "vocabulary":[{"word":"sách","meaning":"Đồ dùng để đọc","example":"Lan đọc sách."}],
                  "quiz":[
                    {"type":"multiple_choice","question":"Ai chia sẻ sách?","options":["Lan","Minh"],"correctOptionIndex":0,"correctAnswer":"Lan","explanation":"Lan chia sẻ sách."},
                    {"type":"true_false","question":"Minh cảm ơn bạn?","options":[],"correctOptionIndex":-1,"correctAnswer":"true","explanation":"Minh cảm ơn bạn."},
                    {"type":"short_answer","question":"Lan chia sẻ gì?","options":[],"correctOptionIndex":-1,"correctAnswer":"sách","explanation":"Lan chia sẻ sách."}
                  ]
                }
                """, "fixture", "fixture-model", "1", 10, 20, 5));
        }
    }
}
