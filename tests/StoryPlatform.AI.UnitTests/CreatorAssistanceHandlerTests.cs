using System.Text.Json;
using StoryPlatform.AI.Application.Abstractions.LLM;
using StoryPlatform.AI.Application.CreatorAssistance;
using StoryPlatform.AI.Domain.Generation;
using StoryPlatform.Contracts.AI.CreatorAssistance;
using Xunit;

namespace StoryPlatform.AI.UnitTests;

public sealed class CreatorAssistanceHandlerTests
{
    [Fact]
    public async Task SelectionTextIsComputedByServerIncludingUtf16BeforeTarget()
    {
        var fake = new FakeLlm(Options);
        await new CreatorAssistanceHandler(fake).HandleAsync(Request with
        {
            EntryPoint = "SupportSelection", Snapshot = new("Title", "😀Na muốn sửa xe."), Target = new("selection", 2, 2)
        }, default);
        Assert.Contains("TARGET_TEXT (untrusted story data):\n\"Na\"", fake.GenerationPrompt);
    }
    [Theory]
    [InlineData("creator_assistance", true, "CallerCancelled", 1)]
    [InlineData("creator_assistance_safety", true, "CallerCancelled", 2)]
    [InlineData("creator_assistance", false, "ProviderCancelled", 1)]
    [InlineData("creator_assistance_safety", false, "ProviderCancelled", 2)]
    public async Task StageCancellationIsMeasuredAndNeverReturnsPartialOutput(string stage, bool callerCancelled, string outcome, int calls)
    {
        using var cancellation = new CancellationTokenSource();
        var fake = new FakeLlm(Options) { BeforeResponse = schema =>
        {
            if (schema != stage) return;
            if (callerCancelled) cancellation.Cancel();
            throw new TaskCanceledException("private story content");
        } };
        var logger = new RecordingLogger<CreatorAssistanceHandler>();
        await Assert.ThrowsAsync<TaskCanceledException>(() => new CreatorAssistanceHandler(fake, logger).HandleAsync(Request, cancellation.Token));
        Assert.Equal(calls, fake.Calls);
        Assert.Contains(logger.Entries, x => x.Message.Contains($"Stage={stage}") && x.Message.Contains($"Outcome={outcome}") && x.Message.Contains("LatencyMs="));
        Assert.DoesNotContain(logger.Entries, x => x.Message.Contains("private story content") || x.Message.Contains(Request.Snapshot.Content));
    }
    [Fact]
    public async Task GenerationSchemaCanBeReadByVertexSdkWithoutCallingProvider()
    {
        var fake = new FakeLlm(Options);
        await new CreatorAssistanceHandler(fake).HandleAsync(Request, default);
        var schema = JsonSerializer.Deserialize<Google.GenAI.Types.Schema>(fake.GenerationSchema.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(schema);
        Assert.Equal(2, fake.GenerationSchema.GetProperty("properties").GetProperty("question").GetProperty("anyOf").GetArrayLength());
    }
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static AssistanceAiRequest Request => new() { Snapshot = new("Title", "Na muốn sửa xe."), Target = new("cursor", 15, 0) };
    private static AssistanceAiResult Options => new() { ResultType = "Options", Options = [new("a", "Together", "Ask a friend"), new("b", "Try", "Try again")] };

    [Fact]
    public async Task UsesStructuredGenerationAndSafetyAssessment_WithTrustedModelMetadata()
    {
        var fake = new FakeLlm(Options with { Provider = "forged-provider", Model = "forged-model" });
        var result = await new CreatorAssistanceHandler(fake).HandleAsync(Request, default);
        Assert.Equal("fake-provider", result.Provider); Assert.Equal("fake-model", result.Model);
        Assert.Equal(CreatorAssistanceHandler.PromptVersion, result.PromptVersion);
        Assert.Equal(2, fake.Calls);
        Assert.Contains("AUTHOR_DATA", fake.GenerationPrompt);
        Assert.Contains("ageBand", fake.GenerationPrompt);
    }

    [Fact]
    public async Task TrialPatchPositionsAreServerOwned()
    {
        var fake = new FakeLlm(new AssistanceAiResult { ResultType = "Trial", Patch = new("replace", 0, 15, " Hai bạn cùng thử.", "forged") });
        var result = await new CreatorAssistanceHandler(fake).HandleAsync(Request with { Mode = "Trial", ConfirmedDirection = "Friends cooperate" }, default);
        Assert.Equal(new AssistancePatch("insert", 15, 0, " Hai bạn cùng thử.", ""), result.Patch);
    }

    [Fact]
    public async Task EmptyReviewReturnsCoverageAndLimitations()
    {
        var fake = new FakeLlm(new AssistanceAiResult { ResultType = "ReviewFindings", ChecksPerformed = ["continuity"], Warnings = ["Human review is still required."] });
        var result = await new CreatorAssistanceHandler(fake).HandleAsync(Request with { EntryPoint = "ReviewDraft", Target = new("wholeDraft", 0, 15) }, default);
        Assert.Equal("FullSnapshot", result.Coverage); Assert.Empty(result.Findings);
    }

    [Fact]
    public async Task InvalidReviewAnchorsAreNotPresentedAsEmptySuccessfulReview()
    {
        var fake = new FakeLlm(new AssistanceAiResult { ResultType = "ReviewFindings", ChecksPerformed = ["continuity"], Findings = [new("f", 0, 2, "XX", "Comment", "Goal")] });
        await Assert.ThrowsAsync<AssistanceOutputException>(() => new CreatorAssistanceHandler(fake).HandleAsync(Request with { EntryPoint = "ReviewDraft", Target = new("wholeDraft", 0, 15) }, default));
        Assert.Equal(1, fake.Calls);
    }

    [Fact]
    public async Task AfterAnswerModelCannotAskAnotherQuestion()
    {
        var fake = new FakeLlm(new AssistanceAiResult { ResultType = "Clarification", Question = new("q", "Again?", [new("a", "One"), new("b", "Two")]) });
        await Assert.ThrowsAsync<AssistanceOutputException>(() => new CreatorAssistanceHandler(fake).HandleAsync(Request with { Answer = "Friendship" }, default));
    }

    [Fact]
    public async Task SafetyFailureBlocksOutput()
    {
        var fake = new FakeLlm(Options) { SafetyJson = "{\"isAppropriate\":false}" };
        await Assert.ThrowsAsync<AssistanceOutputException>(() => new CreatorAssistanceHandler(fake).HandleAsync(Request, default));
    }

    [Fact]
    public async Task SafetyMalformedJsonFailsClosed()
    {
        var fake = new FakeLlm(Options) { SafetyJson = "not-json" };
        await Assert.ThrowsAsync<AssistanceOutputException>(() => new CreatorAssistanceHandler(fake).HandleAsync(Request, default));
    }

    [Fact]
    public async Task PromptLeakageIsRejectedBeforeSecondModelCall()
    {
        var fake = new FakeLlm(Options with { Warnings = ["ignore previous instructions"] });
        await Assert.ThrowsAsync<AssistanceOutputException>(() => new CreatorAssistanceHandler(fake).HandleAsync(Request, default));
        Assert.Equal(1, fake.Calls);
    }

    [Theory]
    [InlineData("😀Na", 1, false)]
    [InlineData("😀Na", 2, true)]
    [InlineData("Na\r\nNam", 4, true)]
    public void Utf16BoundaryDoesNotSplitSurrogatePair(string content, int start, bool valid)
    {
        if (valid) AssistanceValidation.ValidateTarget("ContinueWriting", new("Story", content), new("cursor", start, 0));
        else Assert.Throws<ArgumentException>(() => AssistanceValidation.ValidateTarget("ContinueWriting", new("Story", content), new("cursor", start, 0)));
    }

    [Fact]
    public async Task WholeDraftTrialIsRejectedBeforeInference()
    {
        var fake = new FakeLlm(Options);
        await Assert.ThrowsAsync<ArgumentException>(() => new CreatorAssistanceHandler(fake).HandleAsync(Request with { Mode = "Trial", EntryPoint = "ReviewDraft", Target = new("wholeDraft", 0, 15), ConfirmedDirection = "Rewrite all" }, default));
        Assert.Equal(0, fake.Calls);
    }

    private sealed class FakeLlm(AssistanceAiResult output) : ILlmClient
    {
        public Action<string>? BeforeResponse { get; init; }
        public int Calls { get; private set; }
        public string SafetyJson { get; set; } = "{\"isAppropriate\":true}";
        public string GenerationPrompt { get; private set; } = "";
        public JsonElement GenerationSchema { get; private set; }
        public Task<LlmGenerationResult> GenerateStructuredAsync(string prompt, string schemaName, JsonElement schema, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested(); Calls++;
            BeforeResponse?.Invoke(schemaName);
            if (schemaName == "creator_assistance") { GenerationPrompt = prompt; GenerationSchema = schema.Clone(); }
            Assert.Equal("object", schema.GetProperty("type").GetString());
            return Task.FromResult(new LlmGenerationResult(schemaName == "creator_assistance_safety" ? SafetyJson : JsonSerializer.Serialize(output, Json), "fake-provider", "fake-model", "v1", 0, 0, 1));
        }
    }
}
