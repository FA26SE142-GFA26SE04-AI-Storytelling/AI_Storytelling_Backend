using StoryPlatform.Application.Features.ListeningInteractions.Models;
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
    [InlineData(3, "Checkpointx2,EventOrderingx1")]
    [InlineData(4, "Checkpointx2,RolePlayx1,EventOrderingx1")]
    [InlineData(5, "Checkpointx2,RolePlayx1,PicturePredictionx1,EventOrderingx1")]
    [InlineData(9, "Checkpointx2,RolePlayx1,PicturePredictionx1,EventOrderingx1")]
    public void Age6To8_FollowsSpecTable(int blocks, string expected) =>
        Assert.Equal(expected, Describe(AgeBand.Age_6_8, blocks));

    [Theory]
    [InlineData(3, "Checkpointx2,EventOrderingx1")]
    [InlineData(4, "Checkpointx2,FillBlankx1,EventOrderingx1")]
    [InlineData(5, "Checkpointx2,FillBlankx1,PicturePredictionx1,EventOrderingx1")]
    [InlineData(6, "Checkpointx3,FillBlankx1,PicturePredictionx1,EventOrderingx1")]
    [InlineData(12, "Checkpointx3,FillBlankx1,PicturePredictionx1,EventOrderingx1")]
    public void Age9To10_FollowsSpecTable(int blocks, string expected) =>
        Assert.Equal(expected, Describe(AgeBand.Age_9_10, blocks));

    [Fact]
    public void Age6To8_WithoutDialogue_GivesRolePlaySlotToPicture() =>
        Assert.Equal("Checkpointx2,PicturePredictionx1,EventOrderingx1", Describe(AgeBand.Age_6_8, 5, dialogue: false));

    [Fact]
    public void Plan_ReportsSlotsAndDialogueBlocks()
    {
        var plan = ListeningInteractionPlanner.Create(AgeBand.Age_6_8, ListeningTestStory.Blocks);
        Assert.Equal(5, plan.Slots);
        Assert.Equal(["P2"], plan.DialogueBlockIds);
        Assert.Equal([T.Checkpoint, T.RolePlay, T.PicturePrediction, T.EventOrdering], plan.Items.Select(x => x.Type));
    }

    private static string Zones(ListeningPlanItem item) => string.Join(" | ", item.Zones.Select(z => string.Join(",", z)));

    private static ListeningPlanItem Item(ListeningInteractionPlan plan, T type) => plan.Items.Single(x => x.Type == type);

    [Fact]
    public void Zones_SplitLongStoryEvenly_AndGiveRolePlayTheDialogueZone()
    {
        var plan = ListeningInteractionPlanner.Create(AgeBand.Age_6_8, Story(15)); // thoại ở P1

        Assert.Equal("P1,P2,P3", Zones(Item(plan, T.RolePlay)));
        Assert.Equal("P4,P5,P6,P7 | P8,P9,P10", Zones(Item(plan, T.Checkpoint)));
        Assert.Equal("P11,P12,P13,P14", Zones(Item(plan, T.PicturePrediction)));
        Assert.Empty(Item(plan, T.EventOrdering).Zones);
    }

    [Fact]
    public void Zones_RolePlayPrefersDialogueZoneNearestTheMiddle()
    {
        var text = string.Join("\n\n", Enumerable.Range(1, 15)
            .Select(i => i is 5 or 12 ? $"Thỏ hỏi: \"Đoạn {i} có ai không?\"" : $"Đoạn số {i} kể chuyện."));
        var plan = ListeningInteractionPlanner.Create(AgeBand.Age_6_8, new StoryBlockParser().Parse(text));

        Assert.Equal("P4,P5,P6,P7", Zones(Item(plan, T.RolePlay)));
        Assert.Equal("P1,P2,P3 | P8,P9,P10", Zones(Item(plan, T.Checkpoint)));
        Assert.Equal("P11,P12,P13,P14", Zones(Item(plan, T.PicturePrediction)));
    }

    [Fact]
    public void Zones_OlderChild_FillBlankTakesMiddleZone_CheckpointsFromStart_PictureLast()
    {
        var plan = ListeningInteractionPlanner.Create(AgeBand.Age_9_10, Story(8));

        Assert.Equal("P3,P4", Zones(Item(plan, T.FillBlank)));
        Assert.Equal("P1 | P2 | P5", Zones(Item(plan, T.Checkpoint)));
        Assert.Equal("P6,P7", Zones(Item(plan, T.PicturePrediction)));
    }

    [Fact]
    public void Zones_FillBlankSkipsZonesWithoutShortSentence()
    {
        var longSentence = string.Join(" ", Enumerable.Repeat("chữ", 16)) + ".";
        var text = string.Join("\n\n", Enumerable.Range(1, 8).Select(i => i is 3 or 4 ? longSentence : $"Đoạn số {i} kể chuyện."));
        var plan = ListeningInteractionPlanner.Create(AgeBand.Age_9_10, new StoryBlockParser().Parse(text));

        Assert.Equal("P2", Zones(Item(plan, T.FillBlank))); // P3-P4 không có câu ≤ 15 chữ; hòa khoảng cách thì lấy vùng trước
        Assert.Equal("P1 | P3,P4 | P5", Zones(Item(plan, T.Checkpoint)));
    }

    [Theory]
    [InlineData(AgeBand.Age_6_8)]
    [InlineData(AgeBand.Age_9_10)]
    public void Zones_PartitionEverySlotWithOneZonePerInteraction(AgeBand age)
    {
        for (var blocks = 2; blocks <= 20; blocks++)
        {
            var middle = ListeningInteractionPlanner.Create(age, Story(blocks)).Items.Where(x => x.Type != T.EventOrdering).ToArray();
            Assert.All(middle, x => Assert.Equal(x.Count, x.Zones.Count));
            var zones = middle.SelectMany(x => x.Zones).ToArray();
            Assert.All(zones, z => Assert.NotEmpty(z));
            Assert.Equal(Enumerable.Range(1, blocks - 1).Select(i => $"P{i}"),
                zones.SelectMany(z => z).OrderBy(id => int.Parse(id[1..])));
        }
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
