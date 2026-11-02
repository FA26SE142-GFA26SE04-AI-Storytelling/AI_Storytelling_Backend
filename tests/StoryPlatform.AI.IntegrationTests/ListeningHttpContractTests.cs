using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using StoryPlatform.AI.Api.Controllers;
using StoryPlatform.AI.Api.Middleware;
using StoryPlatform.AI.Application;
using StoryPlatform.AI.Application.Abstractions.LLM;
using StoryPlatform.AI.Application.Abstractions.Prompting;
using StoryPlatform.AI.Domain.Generation;
using StoryPlatform.AI.Infrastructure.PromptCatalog;
using StoryPlatform.Application.Abstractions.AI;
using StoryPlatform.Contracts.AI.Models;
using StoryPlatform.Contracts.AI.Requests;
using StoryPlatform.Infrastructure.AI;
using Xunit;

namespace StoryPlatform.AI.IntegrationTests;

public sealed class ListeningHttpContractTests
{
    [Theory]
    [InlineData("Checkpoint", "6-8")]
    [InlineData("RolePlay", "6-8")]
    [InlineData("FillBlank", "9-10")]
    [InlineData("PicturePrediction", "6-8")]
    [InlineData("EventOrdering", "9-10")]
    public async Task Core_http_adapter_calls_real_ai_routes_and_maps_each_type(string type, string age)
    {
        var model = new Model(Output(type));
        using var server = Server(model);
        var client = Client(server);
        var snapshot = await client.GetListeningPromptSnapshotAsync(type, age, "vi");
        var response = await client.GenerateListeningInteractionAsync(Request(type, age) with
        {
            Snapshot = new() { PromptVersionNo = snapshot.Version, Templates = new Dictionary<string, string> { [snapshot.PromptType] = snapshot.Template } }
        });

        Assert.Equal("listening-http", response.RequestId);
        Assert.Equal(snapshot.Version, response.Metadata.PromptVersion);
        Assert.Equal(1, model.Calls);
        Assert.Equal(1, response.Checkpoints.Count + response.RolePlays.Count + response.FillBlanks.Count + response.PicturePredictions.Count + response.EventOrderings.Count);
        Assert.Contains("previousErrors", model.Prompt);
        Assert.Contains("AllowedBlockIds".ToLowerInvariant(), model.Prompt.ToLowerInvariant());
        Assert.Contains("{{context}}", snapshot.Template);
        Assert.False(model.Schema.GetProperty("additionalProperties").GetBoolean());
    }

    [Theory]
    [InlineData("{\"items\":[]}")]
    [InlineData("{\"items\":[],\"extra\":true}")]
    [InlineData("{\"items\":[{\"afterBlockId\":\"P1\",\"characterName\":\"Thỏ\",\"lineQuote\":null,\"praiseFeedback\":\"Con đã thử!\"}]}")]
    public async Task Schema_rejection_returns_typed_error_without_hidden_model_retries(string json)
    {
        var model = new Model(json);
        using var server = Server(model);
        var error = await Assert.ThrowsAsync<AIServiceRequestException>(() => Client(server).GenerateListeningInteractionAsync(Request("RolePlay", "6-8")));
        Assert.Equal("AI_OUTPUT_INVALID", error.ErrorCode);
        Assert.Equal(1, model.Calls);
    }

    [Fact]
    public async Task Provider_rate_limit_preserves_retry_after_and_is_not_retried_in_ai_service()
    {
        var failure = new HttpRequestException("fixture rate limit", null, HttpStatusCode.TooManyRequests);
        failure.Data["RetryAfterSeconds"] = 7d;
        var model = new Model(failure);
        using var server = Server(model);
        var error = await Assert.ThrowsAsync<AIServiceRequestException>(() => Client(server).GenerateListeningInteractionAsync(Request("RolePlay", "6-8")));
        Assert.Equal(429, error.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(7), error.RetryAfter);
        Assert.Equal(1, model.Calls);
    }

    [Fact]
    public async Task Snapshot_is_used_instead_of_the_current_template()
    {
        var model = new Model(Output("RolePlay"));
        using var server = Server(model);
        await Client(server).GenerateListeningInteractionAsync(Request("RolePlay", "6-8") with
        {
            Snapshot = new() { PromptVersionNo = "pinned-fixture-v0", Templates = new Dictionary<string, string>
                { ["ListeningRolePlay"] = "PINNED FIXTURE {{context}}" } }
        });
        Assert.StartsWith("PINNED FIXTURE", model.Prompt);
    }

    [Fact]
    public async Task Wrong_age_or_missing_internal_key_never_calls_model()
    {
        var model = new Model(Output("FillBlank"));
        using var server = Server(model);
        var invalid = await Assert.ThrowsAsync<AIServiceRequestException>(() => Client(server).GenerateListeningInteractionAsync(Request("FillBlank", "6-8")));
        Assert.Equal(400, invalid.StatusCode);
        var unauthenticated = new AIStoryGenerationClient(server.GetTestClient(), Options.Create(new AIServiceOptions { BaseUrl = "http://localhost" }));
        var denied = await Assert.ThrowsAsync<AIServiceRequestException>(() => unauthenticated.GetListeningPromptSnapshotAsync("Checkpoint", "6-8", "vi"));
        Assert.Equal(401, denied.StatusCode);
        Assert.Equal(0, model.Calls);
    }

    private static AIStoryGenerationClient Client(IHost server) => new(server.GetTestClient(),
        Options.Create(new AIServiceOptions { BaseUrl = "http://localhost", InternalApiKey = "fixture-internal-key" }));

    private static IHost Server(ILlmClient model) => new HostBuilder().ConfigureWebHost(builder => builder.UseTestServer()
        .UseEnvironment("Testing")
        .UseConfiguration(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["AI:InternalApiKey"] = "fixture-internal-key" }).Build())
        .ConfigureServices(services =>
        {
            services.AddLogging(); services.AddAIApplication(); services.AddSingleton(model);
            services.AddSingleton<IPromptTemplateProvider, PromptTemplateProvider>();
            services.AddControllers().AddApplicationPart(typeof(StoryContentGenerationController).Assembly);
        })
        .Configure(app =>
        {
            app.UseMiddleware<AIExceptionHandlingMiddleware>(); app.UseMiddleware<InternalApiKeyMiddleware>();
            app.UseRouting(); app.UseEndpoints(endpoints => endpoints.MapControllers());
        })).Start();

    private static GenerateListeningInteractionRequest Request(string type, string age) => new()
    {
        RequestId = "listening-http", InteractionType = type, Count = 1, AgeBand = age, Language = "vi",
        Story = new() { Title = "Thỏ con", Lesson = "Giúp đỡ" },
        Blocks = [new("P1", "Thỏ hỏi: Có ai ở đó không?"), new("P2", "Bác Gấu giúp Thỏ."), new("P3", "Thỏ trở về.")],
        LastBlockId = "P3", AllowedBlockIds = type == "EventOrdering" ? [] : [["P1"]], DialogueBlockIds = ["P1"]
    };

    private static string Output(string type)
    {
        var item = type switch
        {
            "Checkpoint" => """{"kind":"Opinion","afterBlockId":"P1","question":"Con cảm thấy thế nào?","sourceQuote":"Thỏ hỏi: Có ai ở đó không?","explanation":"Cảm nhận của bé","options":[{"text":"Con thấy lo","feedback":"Mình cùng nghe tiếp nhé!","isMatch":false},{"text":"Con thấy tò mò","feedback":"Mình cùng nghe tiếp nhé!","isMatch":false}],"otherFeedback":"Mình cùng nghe tiếp nhé!"}""",
            "RolePlay" => """{"afterBlockId":"P1","characterName":"Thỏ","lineQuote":"Có ai ở đó không?","praiseFeedback":"Con đã thử rồi!"}""",
            "FillBlank" => """{"afterBlockId":"P1","sentenceQuote":"Thỏ hỏi: Có ai ở đó không?","hiddenWord":"Thỏ","acceptedAnswers":["Thỏ"],"hint":"Bạn có tai dài","matchFeedback":"Con nhớ giỏi quá!","suggestFeedback":"Bạn trong truyện là Thỏ đó!"}""",
            "PicturePrediction" => """{"afterBlockId":"P1","revealBlockId":"P2","question":"Ai sẽ giúp Thỏ?","sourceQuote":"Thỏ hỏi: Có ai ở đó không?","waitFeedback":"Mình nghe tiếp nhé!","options":[{"label":"Bác Gấu","imageDescription":"Bác Gấu hiền lành","feedback":"Bác Gấu giúp Thỏ!","isMatch":true},{"label":"Bạn Sóc","imageDescription":"Bạn Sóc nhỏ","feedback":"Con có ý hay!","isMatch":false}]}""",
            _ => """{"events":[{"order":1,"blockId":"P1","text":"Thỏ hỏi","sourceQuote":"Có ai ở đó không?"},{"order":2,"blockId":"P2","text":"Gấu giúp Thỏ","sourceQuote":"Bác Gấu giúp Thỏ."},{"order":3,"blockId":"P3","text":"Thỏ trở về","sourceQuote":"Thỏ trở về."}],"matchFeedback":"Con nhớ giỏi quá!","retryFeedback":"Mình nghe lại nhé!"}"""
        };
        return "{\"items\":[" + item + "]}";
    }

    private sealed class Model(object result) : ILlmClient
    {
        public int Calls { get; private set; }
        public string Prompt { get; private set; } = string.Empty;
        public JsonElement Schema { get; private set; }
        public Task<LlmGenerationResult> GenerateStructuredAsync(string prompt, string schemaName, JsonElement schema, CancellationToken ct = default)
        {
            Calls++; Prompt = prompt; Schema = schema.Clone();
            return result is Exception error ? Task.FromException<LlmGenerationResult>(error) :
                Task.FromResult(new LlmGenerationResult((string)result, "fixture", "fixture", "1", 10, 20, 1));
        }
    }
}
