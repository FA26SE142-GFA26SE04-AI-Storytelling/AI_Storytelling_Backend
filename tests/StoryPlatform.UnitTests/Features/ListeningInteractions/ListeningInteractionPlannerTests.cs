using StoryPlatform.Application.Features.ListeningInteractions.Services;
using StoryPlatform.Application.Features.MediaGeneration.Models;
using StoryPlatform.Application.Features.MediaGeneration.Services;
using StoryPlatform.Domain.Enums;
using Xunit;
using T = StoryPlatform.Domain.Enums.ListeningInteractionType;

namespace StoryPlatform.UnitTests.Features.ListeningInteractions;

public sealed class ListeningInteractionPlannerTests
{
    // blockCount đoạn, đoạn đầu có thoại khi withDialogue.
    private static IReadOnlyList<StoryTextBlock> Story(int blockCount, bool withDialogue = true) =>
        new StoryBlockParser().Parse(string.Join("\n\n", Enumerable.Range(1, blockCount)
            .Select(i => i == 1 && withDialogue ? "Thỏ hỏi: \"Ai đó?\"" : $"Đoạn số {i} kể chuyện.")));

    private static string Describe(AgeBand age, int blocks, bool dialogue = true) =>
        string.Join(",", ListeningInteractionPlanner.Create(age, Story(blocks, dialogue)).Items.Select(x => $"{x.Type}x{x.Count}"));

    [Theory]
    [InlineData(1, "")]
    [InlineData(2, "Checkpointx1")]
    [InlineData(3, "Checkpointx2")]
    [InlineData(4, "Checkpointx2,EventOrderingx1")]
    [InlineData(5, "Checkpointx2,EventOrderingx1,RolePlayx1")]
    [InlineData(6, "Checkpointx2,EventOrderingx1,RolePlayx1,PicturePredictionx1")]
    [InlineData(9, "Checkpointx2,EventOrderingx1,RolePlayx1,PicturePredictionx1")]
    public void Age6To8_FollowsSpecTable(int blocks, string expected) =>
        Assert.Equal(expected, Describe(AgeBand.Age_6_8, blocks));

    [Theory]
    [InlineData(5, "Checkpointx2,EventOrderingx1,FillBlankx1")]
    [InlineData(6, "Checkpointx2,EventOrderingx1,FillBlankx1,PicturePredictionx1")]
    [InlineData(7, "Checkpointx3,EventOrderingx1,FillBlankx1,PicturePredictionx1")]
    [InlineData(12, "Checkpointx3,EventOrderingx1,FillBlankx1,PicturePredictionx1")]
    public void Age9To10_FollowsSpecTable(int blocks, string expected) =>
        Assert.Equal(expected, Describe(AgeBand.Age_9_10, blocks));

    [Fact]
    public void Age6To8_WithoutDialogue_GivesRolePlaySlotToPicture() =>
        Assert.Equal("Checkpointx2,EventOrderingx1,PicturePredictionx1", Describe(AgeBand.Age_6_8, 5, dialogue: false));

    [Fact]
    public void Plan_ReportsSlotsAndDialogueBlocks()
    {
        var plan = ListeningInteractionPlanner.Create(AgeBand.Age_6_8, ListeningTestStory.Blocks);
        Assert.Equal(5, plan.Slots);
        Assert.Equal(["P2"], plan.DialogueBlockIds);
        Assert.Equal([T.Checkpoint, T.EventOrdering, T.RolePlay, T.PicturePrediction], plan.Items.Select(x => x.Type));
    }

    [Fact]
    public void Dialogue_InLastBlockIsIgnored()
    {
        var blocks = new StoryBlockParser().Parse("Mở đầu.\n\nThỏ hỏi: \"Ai đó?\"");
        Assert.Empty(ListeningInteractionPlanner.FindDialogueBlockIds(blocks));
    }

    [Theory]
    [InlineData("- Ai đó? - Thỏ hỏi.")]
    [InlineData("Thỏ nói “Chào bác”.")]
    [InlineData("Mở đầu.\n— Chào bác!")]
    public void Dialogue_DetectsQuotesAndDashLines(string first)
    {
        var blocks = new StoryBlockParser().Parse(first + "\n\nKết thúc.");
        Assert.Equal(["P1"], ListeningInteractionPlanner.FindDialogueBlockIds(blocks));
    }
}
