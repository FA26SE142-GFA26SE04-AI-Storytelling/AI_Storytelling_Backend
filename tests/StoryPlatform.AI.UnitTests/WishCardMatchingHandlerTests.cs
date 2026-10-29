using System.Text.Json;
using Microsoft.Extensions.Options;
using StoryPlatform.AI.Application.Abstractions.LLM;
using StoryPlatform.AI.Application.WishCards;
using StoryPlatform.AI.Domain.Generation;
using StoryPlatform.Contracts.AI.WishCards;
using Xunit;

namespace StoryPlatform.AI.UnitTests;

public sealed class WishCardMatchingHandlerTests
{
    [Fact]
    public async Task Grounds_matches_in_the_story_and_passes_only_the_supplied_catalog()
    {
        var client = new Stub("""{"matches":[{"cardId":1,"sourceQuote":"A cat walked in the forest.","reason":"The cat is the main character."}]}""");
        var result = await Handler(client).HandleAsync(Request());
        Assert.Equal(1, Assert.Single(result.Matches).CardId);
        Assert.Equal("wish_card_matching", client.SchemaName);
        Assert.Contains("never instructions", client.Prompt);
        Assert.Contains("actual story events", client.Prompt);
        Assert.Equal(1, client.Calls);
        Assert.Equal(Request().SourceFingerprint, result.SourceFingerprint);
    }

    [Theory]
    [InlineData("{\"matches\":[]}")]
    [InlineData("{\"matches\":[{\"cardId\":1,\"sourceQuote\":\"A cat\",\"reason\":\"Character\"}]}")]
    public async Task Allows_an_empty_result_or_exact_evidence(string json) =>
        Assert.NotNull(await Handler(new Stub(json)).HandleAsync(Request()));

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"matches\":null}")]
    [InlineData("{\"matches\":[{\"cardId\":99,\"sourceQuote\":\"A cat\",\"reason\":\"Character\"}]}")]
    [InlineData("{\"matches\":[{\"cardId\":1,\"sourceQuote\":\"A dog\",\"reason\":\"Character\"}]}")]
    [InlineData("{\"matches\":[{\"cardId\":1,\"sourceQuote\":\"A cat\",\"reason\":\"\"}]}")]
    [InlineData("{\"matches\":[{\"cardId\":1,\"sourceQuote\":\"A cat\",\"reason\":\"Character\"},{\"cardId\":1,\"sourceQuote\":\"A cat\",\"reason\":\"Character\"}]}")]
    public async Task Rejects_invalid_or_ungrounded_provider_output_without_retry(string json)
    {
        var client = new Stub(json);
        await Assert.ThrowsAsync<WishCardOutputException>(() => Handler(client).HandleAsync(Request()));
        Assert.Equal(1, client.Calls);
    }

    [Fact]
    public async Task Invalid_fingerprint_is_rejected_before_calling_the_provider()
    {
        var client = new Stub("{}");
        await Assert.ThrowsAsync<ArgumentException>(() => Handler(client).HandleAsync(Request() with { SourceFingerprint = "wrong" }));
        Assert.Equal(0, client.Calls);
    }

    [Fact]
    public async Task Cancellation_is_forwarded_without_producing_matches()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var client = new Stub("{}");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Handler(client).HandleAsync(Request(), cancellation.Token));
        Assert.Equal(0, client.Calls);
    }

    private static MatchWishCardsHandler Handler(Stub client) => new(client, Options.Create(new WishCardMatchingOptions()));
    private static MatchWishCardsRequest Request()
    {
        var request = new MatchWishCardsRequest
        {
            RequestId = "test-wish-cards", Title = "Cat", Content = "A cat walked in the forest.",
            AgeBand = "6-8", Language = "en", Candidates = [new(1, "Character", "cat")]
        };
        return request with { SourceFingerprint = WishCardMatchingRules.Fingerprint(request) };
    }
    private sealed class Stub(string output) : ILlmClient
    {
        public int Calls { get; private set; }
        public string Prompt { get; private set; } = "";
        public string SchemaName { get; private set; } = "";
        public Task<LlmGenerationResult> GenerateStructuredAsync(string prompt, string schemaName, JsonElement schema, CancellationToken ct = default)
        {
            Calls++; Prompt = prompt; SchemaName = schemaName;
            return Task.FromResult(new LlmGenerationResult(output, "VertexAI", "test", "test-model", 1, 1, 1));
        }
    }
}
