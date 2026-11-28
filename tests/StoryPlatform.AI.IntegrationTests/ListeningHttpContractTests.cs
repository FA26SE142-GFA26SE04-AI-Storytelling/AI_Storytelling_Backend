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
using StoryPlatform.AI.Application.ContentGeneration;
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
    [Fact]
    public void Age9_band_has_a_separate_type_catalog()
    {
        Assert.Equal(["CharacterTrial", "FindWrongDetail", "RelationshipMap", "StoryEditor", "WhatIf"],
            ListeningGenerationContract.AllowedTypes("9-10", 9).Order(StringComparer.Ordinal));
        Assert.Equal(5, ListeningGenerationContract.AllowedTypes("Age_9_10", 9).Count);
        Assert.Empty(ListeningGenerationContract.AllowedTypes("9-10", 10));
        Assert.Empty(ListeningGenerationContract.AllowedTypes("6-8", 9));
        Assert.Contains("Checkpoint", ListeningGenerationContract.AllowedTypes("Age_6_8", 6));
        Assert.Contains("PictureSearch", ListeningGenerationContract.AllowedTypes("6-8", 7));
        Assert.Contains("ThoughtBubble", ListeningGenerationContract.AllowedTypes("6-8", 8));
    }

    private static string Age9Item(string type) => type switch
    {
        "CharacterTrial" => """{"afterBlockId":"P1","situation":"Mai ở lại giúp bạn.","decisionQuote":"Mai ở lại.","reasons":[{"text":"Giúp bạn","positive":"Bạn vui.","limitation":"Mai mệt."},{"text":"Giữ hoa","positive":"Hoa an toàn.","limitation":"Mai ướt."},{"text":"Cùng làm","positive":"Hai bạn đoàn kết.","limitation":"Mất thời gian."}],"matchFeedback":"Cảm ơn con."}""",
        "FindWrongDetail" => """{"afterBlockId":"P1","originalQuote":"Mai cầm bình nước. Nam gieo hạt.","retold":"Mai cầm chiếc ô. Nam gieo hạt.","wrongDetail":"chiếc ô","choices":[{"text":"bình nước","isCorrect":true},{"text":"cái xô","isCorrect":false},{"text":"chiếc ô","isCorrect":false}],"explanation":"Mai cầm bình nước.","matchFeedback":"Con tìm đúng rồi!"}""",
        "RelationshipMap" => """{"afterBlockId":"P1","characters":[{"name":"Mai","imageDescription":"Mai trong vườn"},{"name":"Nam","imageDescription":"Nam trong vườn"},{"name":"An","imageDescription":"An trong vườn"}],"cards":["giúp đỡ","bảo vệ","tin tưởng","cản trở"],"relations":[{"from":0,"to":1,"accepted":[0],"evidence":"Mai giúp Nam.","explanation":"Mai giúp Nam."},{"from":1,"to":2,"accepted":[1],"evidence":"Nam bảo vệ An.","explanation":"Nam bảo vệ An."},{"from":2,"to":0,"accepted":[2],"evidence":"An tin Mai.","explanation":"An tin Mai."}],"matchFeedback":"Con nhìn đúng rồi!"}""",
        "StoryEditor" => """{"afterBlockId":"P1","passage":"Một. Hai. Ba. Bốn. Năm.","sentences":[{"text":"Một.","isMain":false,"whyImportant":""},{"text":"Hai.","isMain":true,"whyImportant":"Ý chính."},{"text":"Ba.","isMain":true,"whyImportant":"Ý chính."},{"text":"Bốn.","isMain":false,"whyImportant":""},{"text":"Năm.","isMain":true,"whyImportant":"Kết luận."}],"matchFeedback":"Con giữ đúng ý!"}""",
        _ => """{"afterBlockId":"P1","decisionQuote":"Mai ở lại.","question":"Nếu Mai đi thì điều gì có thể xảy ra?","outcomes":[{"text":"Hoa ướt","because":"Mưa xuống."},{"text":"Nam che hoa","because":"Nam có ô."},{"text":"An giúp Mai","because":"An ở gần."}],"matchFeedback":"Cảm ơn con."}"""
    };

    [Theory]
    [InlineData("CharacterTrial")]
    [InlineData("FindWrongDetail")]
    [InlineData("RelationshipMap")]
    [InlineData("StoryEditor")]
    [InlineData("WhatIf")]
    public async Task Age9_contract_accepts_each_planned_type(string type)
    {
        var model = new Model("{\"items\":[" + Age9Item(type) + "]}");
        using var server = Server(model);
        var response = await Client(server).GenerateListeningInteractionAsync(
            Request(type, "9-10") with { TargetAge = 9, AllowedBlockIds = [["P1"]] });
        Assert.Equal(1, model.Calls);
        Assert.Equal(1, type switch
        {
            "CharacterTrial" => response.CharacterTrials.Count,
            "FindWrongDetail" => response.FindWrongDetails.Count,
            "RelationshipMap" => response.RelationshipMaps.Count,
            "StoryEditor" => response.StoryEditors.Count,
            _ => response.WhatIfs.Count
        });
    }

    [Theory]
    [InlineData("PictureSearch")]
    [InlineData("TrueFalse")]
    [InlineData("FollowAction")]
    [InlineData("SentenceBuilder")]
    [InlineData("EventOrdering")]
    public async Task Age7_contract_accepts_each_planned_type(string type)
    {
        var item = type switch
        {
            "PictureSearch" => """{"afterBlockId":"P1","sourceQuote":"Thỏ hỏi","targetLabel":"Thỏ","question":"Tìm Thỏ","matchFeedback":"Hay quá!","retryFeedback":"Thử lại nhé."}""",
            "TrueFalse" => """{"afterBlockId":"P1","statements":[{"text":"Thỏ hỏi","isTrue":true,"evidenceLabel":"Thỏ"},{"text":"Gấu hỏi","isTrue":false,"evidenceLabel":"Thỏ"}],"matchFeedback":"Hay quá!","retryFeedback":"Thử lại nhé."}""",
            "FollowAction" => """{"afterBlockId":"P1","characterName":"Thỏ","question":"Bé giúp Thỏ nhé","items":[{"name":"Gấu","isCorrect":true,"imageDescription":"Gấu nâu"},{"name":"cây","isCorrect":false,"imageDescription":"Cây xanh"}],"target":{"name":"hang","imageDescription":"Hang trong rừng"},"matchFeedback":"Hay quá!","retryFeedback":"Thử lại nhé."}""",
            "SentenceBuilder" => """{"afterBlockId":"P1","sentenceQuote":"Thỏ hỏi","cards":["Thỏ","đang","hỏi"],"acceptedOrders":[[1,2,3]],"matchFeedback":"Hay quá!","retryFeedback":"Thử lại nhé."}""",
            _ => """{"afterBlockId":"P2","events":[{"order":1,"blockId":"P1","text":"Thỏ hỏi","sourceQuote":"Thỏ hỏi"},{"order":2,"blockId":"P2","text":"Gấu giúp","sourceQuote":"Gấu giúp"},{"order":3,"blockId":"P3","text":"Thỏ về","sourceQuote":"Thỏ về"}],"matchFeedback":"Hay quá!","retryFeedback":"Thử lại nhé."}"""
        };
        var model = new Model("{\"items\":[" + item + "]}");
        using var server = Server(model);
        var request = Request(type, "6-8") with { TargetAge = 7, AllowedBlockIds = type == "EventOrdering" ? [["P2"]] : [["P1"]] };
        var response = await Client(server).GenerateListeningInteractionAsync(request);
        Assert.Equal(1, model.Calls);
        Assert.Equal("listening-http", response.RequestId);
        if (type == "SentenceBuilder") Assert.Equal(3, Assert.Single(response.SentenceBuilders).Cards.Count);
        if (type == "EventOrdering") Assert.Equal("P2", Assert.Single(response.EventOrderings).AfterBlockId);
    }

    [Fact]
    public async Task Age7_TrueFalse_roundtrips_the_evidence_label_of_each_statement()
    {
        var model = new Model("""{"items":[{"afterBlockId":"P1","statements":[{"text":"Thỏ hỏi","isTrue":true,"evidenceLabel":"Thỏ"},{"text":"Gấu hỏi","isTrue":false,"evidenceLabel":"cái tai dài"}],"matchFeedback":"Hay quá!","retryFeedback":"Thử lại nhé."}]}""");
        using var server = Server(model);
        var response = await Client(server).GenerateListeningInteractionAsync(
            Request("TrueFalse", "6-8") with { TargetAge = 7, AllowedBlockIds = [["P1"]] });
        var item = Assert.Single(response.TrueFalses);
        Assert.Equal(["Thỏ", "cái tai dài"], item.Statements.Select(x => x.EvidenceLabel));
        Assert.Equal([true, false], item.Statements.Select(x => x.IsTrue));
        Assert.Contains("evidenceLabel", model.Prompt);
        var statements = model.Schema.GetProperty("properties").GetProperty("items").GetProperty("items")
            .GetProperty("properties").GetProperty("statements").GetProperty("items");
        Assert.Contains("evidenceLabel", statements.GetProperty("required").EnumerateArray().Select(x => x.GetString()));
    }

    [Fact]
    public async Task Age7_TrueFalse_without_an_evidence_label_is_rejected()
    {
        var model = new Model("""{"items":[{"afterBlockId":"P1","statements":[{"text":"Thỏ hỏi","isTrue":true},{"text":"Gấu hỏi","isTrue":false}],"matchFeedback":"Hay quá!","retryFeedback":"Thử lại nhé."}]}""");
        using var server = Server(model);
        var error = await Assert.ThrowsAsync<AIServiceRequestException>(() => Client(server).GenerateListeningInteractionAsync(
            Request("TrueFalse", "6-8") with { TargetAge = 7, AllowedBlockIds = [["P1"]] }));
        Assert.Equal("AI_OUTPUT_INVALID", error.ErrorCode);
        Assert.Equal(1, model.Calls);
    }

    [Theory]
    [InlineData("Checkpoint", 7)]            // Điểm dừng nghĩa đen là của tuổi 6
    [InlineData("PicturePrediction", 7)]
    [InlineData("RolePlay", 7)]
    [InlineData("PictureSearch", 6)]         // Bốn loại mới chỉ của tuổi 7
    [InlineData("TrueFalse", 6)]
    [InlineData("FollowAction", 6)]
    [InlineData("SentenceBuilder", 6)]
    [InlineData("PictureSearch", 8)]         // Tuổi 8 chưa có bộ
    [InlineData("PictureSearch", 10)]
    public async Task Age_specific_types_are_rejected_for_the_wrong_age_without_calling_the_model(string type, int targetAge)
    {
        var model = new Model(Output("RolePlay"));
        using var server = Server(model);
        var error = await Assert.ThrowsAsync<AIServiceRequestException>(() => Client(server).GenerateListeningInteractionAsync(
            Request(type, "6-8") with { TargetAge = targetAge, CheckpointKind = "Literal" }));
        Assert.Equal(400, error.StatusCode);
        Assert.Equal(0, model.Calls);
    }

    [Theory]
    [InlineData("PictureSearch")]
    [InlineData("TrueFalse")]
    [InlineData("FollowAction")]
    [InlineData("SentenceBuilder")]
    [InlineData("EventOrdering")]
    public async Task Age7_types_are_rejected_for_the_older_band(string type)
    {
        var model = new Model(Output("RolePlay"));
        using var server = Server(model);
        var error = await Assert.ThrowsAsync<AIServiceRequestException>(() => Client(server).GenerateListeningInteractionAsync(
            Request(type, "9-10") with { TargetAge = 7 }));
        Assert.Equal(400, error.StatusCode);
        Assert.Equal(0, model.Calls);
    }

    [Fact]
    public async Task Age7_EventOrdering_needs_anchor_zones_but_legacy_EventOrdering_does_not()
    {
        var model = new Model(Output("EventOrdering"));
        using var server = Server(model);
        var missingZone = await Assert.ThrowsAsync<AIServiceRequestException>(() => Client(server).GenerateListeningInteractionAsync(
            Request("EventOrdering", "6-8") with { TargetAge = 7, AllowedBlockIds = [] }));
        Assert.Equal(400, missingZone.StatusCode);
        Assert.Equal(0, model.Calls);
        await Client(server).GenerateListeningInteractionAsync(Request("EventOrdering", "6-8"));     // bộ cũ: neo ở cuối, không cần vùng
        Assert.Equal(1, model.Calls);
    }

    [Theory]
    [InlineData("PictureSearch", "ListeningPictureSearch")]
    [InlineData("TrueFalse", "ListeningTrueFalse")]
    [InlineData("FollowAction", "ListeningFollowAction")]
    [InlineData("SentenceBuilder", "ListeningSentenceBuilder")]
    [InlineData("EventOrdering:Age7", "ListeningEventOrderingAge7")]
    public async Task Age7_snapshot_selects_the_age_specific_template(string type, string promptType)
    {
        using var server = Server(new Model(Output("RolePlay")));
        var snapshot = await Client(server).GetListeningPromptSnapshotAsync(type, "6-8", "vi");
        Assert.Equal(promptType, snapshot.PromptType);
        Assert.Contains("{{context}}", snapshot.Template);
    }

    [Theory]
    [InlineData("PictureSearch")]
    [InlineData("EventOrdering:Age7")]
    public async Task Age7_snapshot_rejects_the_older_band(string type)
    {
        var model = new Model(Output("RolePlay"));
        using var server = Server(model);
        var error = await Assert.ThrowsAsync<AIServiceRequestException>(() => Client(server).GetListeningPromptSnapshotAsync(type, "9-10", "vi"));
        Assert.Equal(400, error.StatusCode);
        Assert.Equal(0, model.Calls);
    }

    [Theory]
    [InlineData("""{"afterBlockId":"P1","sentenceQuote":"Thỏ hỏi","cards":["Thỏ","hỏi"],"acceptedOrders":[[1,2]],"matchFeedback":"Hay quá!","retryFeedback":"Thử lại nhé."}""")]
    [InlineData("""{"afterBlockId":"P1","sentenceQuote":"Thỏ hỏi","cards":["a","b","c","d","e","f"],"acceptedOrders":[[1,2,3,4,5,6]],"matchFeedback":"Hay quá!","retryFeedback":"Thử lại nhé."}""")]
    [InlineData("""{"afterBlockId":"P1","sentenceQuote":"Thỏ hỏi","cards":["Thỏ","đang","hỏi"],"acceptedOrders":[],"matchFeedback":"Hay quá!","retryFeedback":"Thử lại nhé."}""")]
    [InlineData("""{"afterBlockId":"P1","sentenceQuote":"Thỏ hỏi","cards":["Thỏ","đang","hỏi"],"acceptedOrders":[[1,2,9]],"matchFeedback":"Hay quá!","retryFeedback":"Thử lại nhé."}""")]
    public async Task Age7_SentenceBuilder_rejects_card_counts_and_orders_outside_the_schema(string item)
    {
        var model = new Model("{\"items\":[" + item + "]}");
        using var server = Server(model);
        var error = await Assert.ThrowsAsync<AIServiceRequestException>(() => Client(server).GenerateListeningInteractionAsync(
            Request("SentenceBuilder", "6-8") with { TargetAge = 7, AllowedBlockIds = [["P1"]] }));
        Assert.Equal("AI_OUTPUT_INVALID", error.ErrorCode);
        Assert.Equal(1, model.Calls);
    }

    [Theory]
    [InlineData("CharacterLetter", "9-10", 6)]
    [InlineData("WhoSaidIt", "6-8", 7)]
    [InlineData("WhoSaidIt", "6-8", null)]
    [InlineData("ReadAloud", "6-8", 6)]
    public async Task Unsupported_age_sets_are_rejected_without_calling_the_model(string type, string band, int? targetAge)
    {
        var model = new Model(Output("RolePlay"));
        using var server = Server(model);
        var error = await Assert.ThrowsAsync<AIServiceRequestException>(() => Client(server).GenerateListeningInteractionAsync(
            Request(type, band) with { TargetAge = targetAge }));
        Assert.Equal(400, error.StatusCode);
        Assert.Equal(0, model.Calls);
    }

    [Theory]
    [InlineData("Checkpoint:Age6")]
    [InlineData("PicturePrediction:Age6")]
    public async Task Age6_snapshot_rejects_the_older_band(string type)
    {
        var model = new Model(Output("RolePlay"));
        using var server = Server(model);
        var error = await Assert.ThrowsAsync<AIServiceRequestException>(() => Client(server).GetListeningPromptSnapshotAsync(type, "9-10", "vi"));
        Assert.Equal(400, error.StatusCode);
        Assert.Equal(0, model.Calls);
    }

    [Theory]
    [InlineData("CharacterLetter")]
    [InlineData("WhoSaidIt")]
    public async Task Age6_contract_roundtrips_new_fields_and_uses_pinned_template(string type)
    {
        var output = type == "CharacterLetter"
            ? """{"items":[{"afterBlockId":"P1","characterName":"Rabbit","imageDescription":"White rabbit","blankIndex":0,"options":[{"text":"th","isMatch":true},{"text":"b","isMatch":false},{"text":"m","isMatch":false}],"praiseFeedback":"Good effort!"}]}"""
            : """{"items":[{"afterBlockId":"P1","sourceQuote":"Hello","speakers":[{"name":"Rabbit","isMatch":true,"imageDescription":"White rabbit"},{"name":"Bear","isMatch":false,"imageDescription":"Brown bear"}],"matchFeedback":"Good effort!","retryFeedback":"Listen again."}]}""";
        var model = new Model(output);
        using var server = Server(model);
        var client = Client(server);
        var snapshot = await client.GetListeningPromptSnapshotAsync(type, "6-8", "vi");
        var response = await client.GenerateListeningInteractionAsync(Request(type, "6-8") with
        {
            TargetAge = 6, AllowedLetters = ["th", "b", "m"],
            Snapshot = new() { PromptVersionNo = snapshot.Version, Templates = new Dictionary<string, string> { [snapshot.PromptType] = snapshot.Template } }
        });
        Assert.Contains("targetAge", model.Prompt);
        if (type == "CharacterLetter")
        {
            var item = Assert.Single(response.CharacterLetters);
            Assert.Equal(0, item.BlankIndex);
            Assert.Equal("White rabbit", item.ImageDescription);
        }
        else Assert.Equal("Rabbit", Assert.Single(response.WhoSaidIts).Speakers[0].Name);
    }

    [Theory]
    [InlineData("Checkpoint:Age6", "ListeningCheckpointLiteral")]
    [InlineData("PicturePrediction:Age6", "ListeningPicturePredictionAge6")]
    public async Task Age6_snapshot_selects_the_age_specific_template(string type, string promptType)
    {
        using var server = Server(new Model(Output("RolePlay")));
        var snapshot = await Client(server).GetListeningPromptSnapshotAsync(type, "6-8", "vi");
        Assert.Equal(promptType, snapshot.PromptType);
    }

    [Theory]
    [InlineData("Checkpoint", "6-8")]
    [InlineData("RolePlay", "6-8")]
    [InlineData("FillBlank", "9-10")]
    [InlineData("PicturePrediction", "6-8")]
    [InlineData("EventOrdering", "9-10")]
    [InlineData("ReadAloud", "6-8")]
    [InlineData("ReadAloud", "9-10")]
    [InlineData("ReadAloud", "Age_6_8")]
    [InlineData("ReadAloud", "Age_9_10")]
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
        Assert.Equal(1, response.Checkpoints.Count + response.RolePlays.Count + response.FillBlanks.Count + response.PicturePredictions.Count + response.EventOrderings.Count + response.ReadAloudPassages.Count);
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

    [Theory]
    [InlineData("{\"items\":[{\"blockId\":\"P1\",\"passageQuote\":\"Thỏ hỏi: Có ai ở đó không?\"}]}")]
    [InlineData("{\"items\":[{\"blockId\":\"P1\",\"passageQuote\":\"Thỏ hỏi: Có ai ở đó không?\",\"praiseFeedback\":\"Con đã thử rồi!\",\"afterBlockId\":\"P3\"}]}")]
    public async Task ReadAloud_rejects_missing_feedback_and_extra_anchor_fields(string output)
    {
        var model = new Model(output);
        using var server = Server(model);
        var error = await Assert.ThrowsAsync<AIServiceRequestException>(() => Client(server).GenerateListeningInteractionAsync(Request("ReadAloud", "6-8")));
        Assert.Equal("AI_OUTPUT_INVALID", error.ErrorCode);
        Assert.Equal(1, model.Calls);
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

    private static string Age8Item(string type) => type switch
    {
        "AlternateReading" => """{"afterBlockId":"P1","lines":[{"speaker":"Thỏ","text":"Có ai ở đó không?"},{"speaker":"Gấu","text":"Bác đây!"}],"praiseFeedback":"Con đọc cùng bạn giỏi quá!"}""",
        "InferenceQuestion" => """{"afterBlockId":"P1","passage":"Thỏ run lẩy bẩy. Thỏ nghe tiếng sói hú.","question":"Vì sao Thỏ trốn?","answers":[{"text":"Vì Thỏ sợ","isCorrect":true},{"text":"Vì Thỏ buồn ngủ","isCorrect":false},{"text":"Vì Thỏ đang chơi","isCorrect":false}],"clues":["run lẩy bẩy","tiếng sói hú","Thỏ nghe"],"explanation":"Thỏ run và nghe tiếng sói hú.","matchFeedback":"Con suy luận giỏi quá!"}""",
        "CauseEffect" => """{"afterBlockId":"P1","pairs":[{"effect":{"text":"Thỏ trốn","sourceQuote":"Thỏ trốn"},"cause":{"text":"Sói hú","sourceQuote":"Sói hú"}},{"effect":{"text":"Gấu giúp","sourceQuote":"Gấu giúp"},"cause":{"text":"Thỏ lạc","sourceQuote":"Thỏ lạc"}},{"effect":{"text":"Thỏ về","sourceQuote":"Thỏ về"},"cause":{"text":"Gấu dắt","sourceQuote":"Gấu dắt"}}],"matchFeedback":"Con nối giỏi quá!"}""",
        "SceneTitle" => """{"afterBlockId":"P1","titles":[{"text":"Thỏ gặp Gấu","isCorrect":true,"feedback":"Tiêu đề này nói về cả cảnh."},{"text":"Thỏ hỏi","isCorrect":false,"feedback":"Tiêu đề này chỉ nói một chi tiết."},{"text":"Trời tối","isCorrect":false,"feedback":"Tiêu đề này chỉ nói một chi tiết."}],"explanation":"Cả cảnh là Thỏ gặp Gấu.","matchFeedback":"Con chọn hay quá!"}""",
        _ => """{"afterBlockId":"P1","characterName":"Thỏ","question":"Thỏ đang nghĩ gì?","sourceQuote":"Thỏ hỏi","bubbles":[{"text":"Mình sợ quá","isCorrect":true,"emotion":"sợ"},{"text":"Mình vui quá","isCorrect":false,"emotion":"vui"},{"text":"Mình giận quá","isCorrect":false,"emotion":"giận"}],"matchFeedback":"Con đoán đúng rồi!"}"""
    };

    [Theory]
    [InlineData("AlternateReading")]
    [InlineData("InferenceQuestion")]
    [InlineData("CauseEffect")]
    [InlineData("SceneTitle")]
    [InlineData("ThoughtBubble")]
    public async Task Age8_contract_accepts_each_planned_type(string type)
    {
        var model = new Model("{\"items\":[" + Age8Item(type) + "]}");
        using var server = Server(model);
        var response = await Client(server).GenerateListeningInteractionAsync(
            Request(type, "6-8") with { TargetAge = 8, AllowedBlockIds = [["P1"]] });
        Assert.Equal(1, model.Calls);
        switch (type)
        {
            case "AlternateReading": Assert.Equal(2, Assert.Single(response.AlternateReadings).Lines.Count); break;
            case "InferenceQuestion":
                var inference = Assert.Single(response.InferenceQuestions);
                Assert.Equal(3, inference.Clues.Count);
                Assert.Equal(3, inference.Answers.Count);
                break;
            case "CauseEffect": Assert.Equal(3, Assert.Single(response.CauseEffects).Pairs.Count); break;
            case "SceneTitle": Assert.Equal(3, Assert.Single(response.SceneTitles).Titles.Count); break;
            default: Assert.Equal("sợ", Assert.Single(response.ThoughtBubbles).Bubbles[0].Emotion); break;
        }
    }

    [Fact]
    public async Task Age8_InferenceQuestion_accepts_two_items_and_other_types_do_not()
    {
        var model = new Model("{\"items\":[" + Age8Item("InferenceQuestion") + "," + Age8Item("InferenceQuestion") + "]}");
        using var server = Server(model);
        var response = await Client(server).GenerateListeningInteractionAsync(
            Request("InferenceQuestion", "6-8") with { TargetAge = 8, Count = 2, AllowedBlockIds = [["P1"], ["P2"]] });
        Assert.Equal(2, response.InferenceQuestions.Count);

        var error = await Assert.ThrowsAsync<AIServiceRequestException>(() => Client(server).GenerateListeningInteractionAsync(
            Request("SceneTitle", "6-8") with { TargetAge = 8, Count = 2, AllowedBlockIds = [["P1"], ["P2"]] }));
        Assert.Equal(400, error.StatusCode);
    }

    [Theory]
    [InlineData("AlternateReading", 6)]
    [InlineData("InferenceQuestion", 7)]
    [InlineData("CauseEffect", 6)]
    [InlineData("SceneTitle", 7)]
    [InlineData("ThoughtBubble", 10)]
    [InlineData("Checkpoint", 8)]
    [InlineData("TrueFalse", 8)]
    [InlineData("EventOrdering", 8)]
    public async Task Age8_types_and_other_ages_do_not_mix(string type, int targetAge)
    {
        var model = new Model(Output("RolePlay"));
        using var server = Server(model);
        var error = await Assert.ThrowsAsync<AIServiceRequestException>(() => Client(server).GenerateListeningInteractionAsync(
            Request(type, "6-8") with { TargetAge = targetAge, CheckpointKind = "Literal" }));
        Assert.Equal(400, error.StatusCode);
        Assert.Equal(0, model.Calls);
    }

    [Theory]
    [InlineData("AlternateReading")]
    [InlineData("InferenceQuestion")]
    [InlineData("CauseEffect")]
    [InlineData("SceneTitle")]
    [InlineData("ThoughtBubble")]
    public async Task Age8_types_are_rejected_for_the_older_band(string type)
    {
        var model = new Model(Output("RolePlay"));
        using var server = Server(model);
        var error = await Assert.ThrowsAsync<AIServiceRequestException>(() => Client(server).GenerateListeningInteractionAsync(
            Request(type, "9-10") with { TargetAge = 8 }));
        Assert.Equal(400, error.StatusCode);
        Assert.Equal(0, model.Calls);
    }

    [Theory]
    [InlineData("AlternateReading", "ListeningAlternateReading")]
    [InlineData("InferenceQuestion", "ListeningInferenceQuestion")]
    [InlineData("CauseEffect", "ListeningCauseEffect")]
    [InlineData("SceneTitle", "ListeningSceneTitle")]
    [InlineData("ThoughtBubble", "ListeningThoughtBubble")]
    public async Task Age8_snapshot_selects_the_age_specific_template(string type, string promptType)
    {
        using var server = Server(new Model(Output("RolePlay")));
        var snapshot = await Client(server).GetListeningPromptSnapshotAsync(type, "6-8", "vi");
        Assert.Equal(promptType, snapshot.PromptType);
        Assert.Contains("{{context}}", snapshot.Template);
    }

    [Theory]
    [InlineData("""{"afterBlockId":"P1","characterName":"Thỏ","question":"Thỏ đang nghĩ gì?","sourceQuote":"Thỏ hỏi","bubbles":[{"text":"a","isCorrect":true,"emotion":"giận dữ"},{"text":"b","isCorrect":false,"emotion":"vui"},{"text":"c","isCorrect":false,"emotion":"sợ"}],"matchFeedback":"Hay!"}""")]
    [InlineData("""{"afterBlockId":"P1","characterName":"Thỏ","question":"Thỏ đang nghĩ gì?","sourceQuote":"Thỏ hỏi","bubbles":[{"text":"a","isCorrect":true,"emotion":"sợ"},{"text":"b","isCorrect":false,"emotion":"vui"}],"matchFeedback":"Hay!"}""")]
    public async Task Age8_ThoughtBubble_rejects_unknown_emotions_and_wrong_bubble_counts(string item)
    {
        var model = new Model("{\"items\":[" + item + "]}");
        using var server = Server(model);
        var error = await Assert.ThrowsAsync<AIServiceRequestException>(() => Client(server).GenerateListeningInteractionAsync(
            Request("ThoughtBubble", "6-8") with { TargetAge = 8, AllowedBlockIds = [["P1"]] }));
        Assert.Equal("AI_OUTPUT_INVALID", error.ErrorCode);
        Assert.Equal(1, model.Calls);
    }

    [Theory]
    [InlineData("""{"afterBlockId":"P1","passage":"Thỏ run. Thỏ sợ.","question":"Vì sao?","answers":[{"text":"a","isCorrect":true},{"text":"b","isCorrect":false},{"text":"c","isCorrect":false}],"clues":["run","sợ"],"explanation":"x","matchFeedback":"Hay!"}""")]
    [InlineData("""{"afterBlockId":"P1","pairs":[{"effect":{"text":"a","sourceQuote":"a"},"cause":{"text":"b","sourceQuote":"b"}}],"matchFeedback":"Hay!"}""")]
    public async Task Age8_schema_rejects_missing_clues_and_missing_pairs(string item)
    {
        var type = item.Contains("\"pairs\"") ? "CauseEffect" : "InferenceQuestion";
        var model = new Model("{\"items\":[" + item + "]}");
        using var server = Server(model);
        var error = await Assert.ThrowsAsync<AIServiceRequestException>(() => Client(server).GenerateListeningInteractionAsync(
            Request(type, "6-8") with { TargetAge = 8, AllowedBlockIds = [["P1"]] }));
        Assert.Equal("AI_OUTPUT_INVALID", error.ErrorCode);
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
        LastBlockId = "P3", AllowedBlockIds = type is "EventOrdering" or "ReadAloud" ? [] : [["P1"]], DialogueBlockIds = ["P1"]
    };

    private static string Output(string type)
    {
        var item = type switch
        {
            "Checkpoint" => """{"kind":"Opinion","afterBlockId":"P1","question":"Con cảm thấy thế nào?","sourceQuote":"Thỏ hỏi: Có ai ở đó không?","explanation":"Cảm nhận của bé","options":[{"text":"Con thấy lo","feedback":"Mình cùng nghe tiếp nhé!","isMatch":false},{"text":"Con thấy tò mò","feedback":"Mình cùng nghe tiếp nhé!","isMatch":false}],"otherFeedback":"Mình cùng nghe tiếp nhé!"}""",
            "RolePlay" => """{"afterBlockId":"P1","characterName":"Thỏ","lineQuote":"Có ai ở đó không?","praiseFeedback":"Con đã thử rồi!"}""",
            "FillBlank" => """{"afterBlockId":"P1","sentenceQuote":"Thỏ hỏi: Có ai ở đó không?","hiddenWord":"Thỏ","acceptedAnswers":["Thỏ"],"hint":"Bạn có tai dài","matchFeedback":"Con nhớ giỏi quá!","suggestFeedback":"Bạn trong truyện là Thỏ đó!"}""",
            "PicturePrediction" => """{"afterBlockId":"P1","revealBlockId":"P2","question":"Ai sẽ giúp Thỏ?","sourceQuote":"Thỏ hỏi: Có ai ở đó không?","waitFeedback":"Mình nghe tiếp nhé!","options":[{"label":"Bác Gấu","imageDescription":"Bác Gấu hiền lành","feedback":"Bác Gấu giúp Thỏ!","isMatch":true},{"label":"Bạn Sóc","imageDescription":"Bạn Sóc nhỏ","feedback":"Con có ý hay!","isMatch":false}]}""",
            "ReadAloud" => """{"blockId":"P1","passageQuote":"Thỏ hỏi: Có ai ở đó không?","praiseFeedback":"Con đã thử rồi!"}""",
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
