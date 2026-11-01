using System.Text.Json;
using StoryPlatform.Application.Features.ListeningInteractions.Models;
using StoryPlatform.Application.Features.ListeningInteractions.Services;
using StoryPlatform.Contracts.AI.Models;
using StoryPlatform.Domain.Enums;
using Xunit;
using S = StoryPlatform.UnitTests.Features.ListeningInteractions.ListeningTestStory;
using T = StoryPlatform.Domain.Enums.ListeningInteractionType;

namespace StoryPlatform.UnitTests.Features.ListeningInteractions;

public sealed class ListeningInteractionAiMapperTests
{
    [Theory]
    [InlineData(T.Checkpoint, 2, AgeBand.Age_6_8)]
    [InlineData(T.RolePlay, 1, AgeBand.Age_6_8)]
    [InlineData(T.FillBlank, 1, AgeBand.Age_9_10)]
    [InlineData(T.EventOrdering, 1, AgeBand.Age_6_8)]
    [InlineData(T.PicturePrediction, 1, AgeBand.Age_6_8)]
    public void ValidAiResponse_PassesMapperAndRules(T type, int count, AgeBand age)
    {
        var mapped = ListeningInteractionMapper.Resolve(ListeningInteractionAiMapper.ToDrafts(S.AiResponse(type), type), S.Content, S.Blocks);
        Assert.Empty(mapped.Errors);
        Assert.Empty(ListeningInteractionRules.Validate(mapped.Items, S.Context(type, count, age)));
    }

    [Fact]
    public void ToDrafts_MapsAiFieldsOntoDraft()
    {
        var checkpoint = ListeningInteractionAiMapper.ToDrafts(S.AiResponse(T.Checkpoint), T.Checkpoint)[0];
        Assert.Equal(T.Checkpoint, checkpoint.Type);
        Assert.Equal("P1", checkpoint.AnchorBlockId);
        Assert.Equal(CheckpointKind.Emotion, checkpoint.Kind);
        Assert.Equal("Trong rừng tối, con nghĩ gì về Thỏ?", checkpoint.Prompt);
        Assert.Equal("Con nghĩ thú vị quá! Cùng nghe tiếp nhé.", checkpoint.NoMatchFeedback);
        Assert.True(checkpoint.Options[0].IsMatch);

        var rolePlay = Assert.Single(ListeningInteractionAiMapper.ToDrafts(S.AiResponse(T.RolePlay), T.RolePlay));
        Assert.Equal("Thỏ", rolePlay.CharacterName);
        Assert.Equal("Có ai ở đó không?", rolePlay.SourceQuote);
        Assert.Equal("Con đã thử nói giống Thỏ rồi, giỏi lắm!", rolePlay.MatchFeedback);

        var fill = Assert.Single(ListeningInteractionAiMapper.ToDrafts(S.AiResponse(T.FillBlank), T.FillBlank));
        Assert.Equal(["Gấu", "bác Gấu"], fill.Options.Select(o => o.Text));
        Assert.Equal("Từ trong truyện là \"Gấu\" đó. Cùng nghe tiếp nhé!", fill.NoMatchFeedback);

        var ordering = Assert.Single(ListeningInteractionAiMapper.ToDrafts(S.AiResponse(T.EventOrdering), T.EventOrdering));
        Assert.Equal([1, 2, 3], ordering.Options.Select(o => o.CorrectOrder!.Value));
        Assert.Equal(["P1", "P3", "P6"], ordering.Options.Select(o => o.BlockId!));
        Assert.Equal(string.Empty, ordering.AnchorBlockId);
        Assert.Equal("Mình cùng nghe lại mấy đoạn này rồi xếp lại nhé.", ordering.NoMatchFeedback);

        var picture = Assert.Single(ListeningInteractionAiMapper.ToDrafts(S.AiResponse(T.PicturePrediction), T.PicturePrediction));
        Assert.Equal("P4", picture.AnchorBlockId);
        Assert.Equal("P5", picture.RevealBlockId);
        Assert.Equal("Mẹ Thỏ", picture.Options[0].Text);
        Assert.Equal("Thỏ mẹ lông trắng đứng trước cửa hang", picture.Options[0].ImageDescription);
    }

    [Theory]
    [InlineData("Mood")]
    [InlineData("1")]
    [InlineData(null)]
    public void ToDrafts_UnknownKindBecomesNull(string? kind)
    {
        var response = S.AiResponse(T.Checkpoint);
        response = response with { Checkpoints = [response.Checkpoints[0] with { Kind = kind }] };
        Assert.Null(Assert.Single(ListeningInteractionAiMapper.ToDrafts(response, T.Checkpoint)).Kind);
    }

    [Fact]
    public void ToDrafts_ReadsOnlyTheRequestedList()
    {
        var response = S.AiResponse(T.Checkpoint) with { RolePlays = S.AiResponse(T.RolePlay).RolePlays };
        Assert.Equal(2, ListeningInteractionAiMapper.ToDrafts(response, T.Checkpoint).Count);
        Assert.Empty(ListeningInteractionAiMapper.ToDrafts(S.AiResponse(T.Checkpoint), T.RolePlay));
    }

    [Fact]
    public void BuildRequest_SendsBlocksOccupiedAndOnlyTypeSpecificLists()
    {
        var occupied = new[] { S.Block("P1").EndOffset, S.Block("P3").EndOffset };
        var errors = new[] { new ListeningValidationError("ANCHOR_INVALID", 0, "afterBlockId") };
        var rolePlay = ListeningInteractionAiMapper.BuildRequest("r-1", T.RolePlay, 1, AgeBand.Age_6_8, "vi",
            "Thỏ con lạc đường", "Biết nhờ người lớn", S.Blocks, ["P2"], occupied, [["P2"]], ["Gấu"], errors);

        Assert.Equal("r-1", rolePlay.RequestId);
        Assert.Equal("RolePlay", rolePlay.InteractionType);
        Assert.Equal(1, rolePlay.Count);
        Assert.Equal("Age_6_8", rolePlay.AgeBand);
        Assert.Equal("Thỏ con lạc đường", rolePlay.Story.Title);
        Assert.Equal(["P1", "P2", "P3", "P4", "P5", "P6"], rolePlay.Blocks.Select(b => b.BlockId));
        Assert.Equal("Thỏ con đi lạc vào rừng. Trời bắt đầu tối.", rolePlay.Blocks[0].Text); // bỏ dòng trống cuối đoạn
        Assert.Equal("P6", rolePlay.LastBlockId);
        Assert.Equal(["P1", "P3"], rolePlay.OccupiedBlockIds);
        Assert.Equal(["P2"], rolePlay.DialogueBlockIds);
        Assert.Equal(["P2"], Assert.Single(rolePlay.AllowedBlockIds));
        Assert.Empty(rolePlay.VocabularyTerms);
        Assert.Equal([new ListeningPreviousErrorDto("ANCHOR_INVALID", 0, "afterBlockId")], rolePlay.PreviousErrors);

        var fill = ListeningInteractionAiMapper.BuildRequest("r-2", T.FillBlank, 1, AgeBand.Age_9_10, "vi",
            "Thỏ con lạc đường", "Biết nhờ người lớn", S.Blocks, ["P2"], [], [["P3", "P4"]], ["Gấu"], []);
        Assert.Equal(["Gấu"], fill.VocabularyTerms);
        Assert.Empty(fill.DialogueBlockIds);
        Assert.Empty(fill.OccupiedBlockIds);
        Assert.Empty(fill.PreviousErrors);
    }

    [Fact]
    public void Contract_UsesAiJsonFieldNames()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        string Json(T type) => JsonSerializer.Serialize(S.AiResponse(type), options);

        Assert.All(new[] { "\"checkpoints\"", "\"kind\"", "\"afterBlockId\"", "\"question\"", "\"sourceQuote\"", "\"otherFeedback\"", "\"isMatch\"" },
            field => Assert.Contains(field, Json(T.Checkpoint)));
        Assert.All(new[] { "\"rolePlays\"", "\"characterName\"", "\"lineQuote\"", "\"praiseFeedback\"" },
            field => Assert.Contains(field, Json(T.RolePlay)));
        Assert.All(new[] { "\"fillBlanks\"", "\"sentenceQuote\"", "\"hiddenWord\"", "\"acceptedAnswers\"", "\"hint\"", "\"suggestFeedback\"" },
            field => Assert.Contains(field, Json(T.FillBlank)));
        Assert.All(new[] { "\"eventOrderings\"", "\"events\"", "\"order\"", "\"blockId\"", "\"retryFeedback\"" },
            field => Assert.Contains(field, Json(T.EventOrdering)));
        Assert.DoesNotContain("\"afterBlockId\"", Json(T.EventOrdering));
        Assert.All(new[] { "\"picturePredictions\"", "\"revealBlockId\"", "\"waitFeedback\"", "\"label\"", "\"imageDescription\"" },
            field => Assert.Contains(field, Json(T.PicturePrediction)));
    }
}
