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

    private static string DescribeAge6(int blocks, bool dialogue = true) =>
        string.Join(",", ListeningInteractionPlanner.Create(6, Story(blocks, dialogue)).Items
            .Select(x => $"{x.Type}{(x.Kind is { } k ? ":" + k : "")}x{x.Count}"));

    [Theory]
    [InlineData(1, "")]
    [InlineData(2, "Checkpoint:Literalx1")]
    [InlineData(3, "Checkpoint:Literalx1,RolePlayx1")]
    [InlineData(4, "Checkpoint:Literalx1,RolePlayx1,CharacterLetterx1")]
    [InlineData(5, "Checkpoint:Literalx1,RolePlayx1,CharacterLetterx1,PicturePredictionx1")]
    [InlineData(6, "Checkpoint:Literalx1,RolePlayx1,CharacterLetterx1,PicturePredictionx1,WhoSaidItx1")]
    [InlineData(12, "Checkpoint:Literalx1,RolePlayx1,CharacterLetterx1,PicturePredictionx1,WhoSaidItx1")]
    public void Age6_FiveDistinctTypes(int blocks, string expected) =>
        Assert.Equal(expected, DescribeAge6(blocks));

    [Fact]
    public void Age6_WithoutDialogue_ReplacesDialogueTypesWithLiteral() =>
        Assert.Equal("Checkpoint:Literalx2,CharacterLetterx1,PicturePredictionx1", DescribeAge6(6, dialogue: false));

    [Fact]
    public void Age6_HasNoEndOfStoryItems() =>
        Assert.DoesNotContain(ListeningInteractionPlanner.Create(6, Story(9)).Items,
            x => ListeningInteractionRules.IsEndOfStory(x.Type));

    [Fact]
    public void Age6_PlanCarriesTargetAge() =>
        Assert.Equal(6, ListeningInteractionPlanner.Create(6, Story(6)).TargetAge);

    [Theory]
    [InlineData(9)]
    [InlineData(10)]
    public void UnbuiltAge_HasEmptyPlan(int age) =>
        Assert.Empty(ListeningInteractionPlanner.Create(age, Story(6)).Items);

    private static string DescribeAge8(int blocks, bool dialogue = true) =>
        string.Join(",", ListeningInteractionPlanner.Create(8, Story(blocks, dialogue)).Items.Select(x => $"{x.Type}x{x.Count}"));

    [Theory]
    [InlineData(1, "")]
    [InlineData(2, "AlternateReadingx1")]
    [InlineData(3, "AlternateReadingx1,InferenceQuestionx1")]
    [InlineData(5, "AlternateReadingx1,InferenceQuestionx1,SceneTitlex1,ThoughtBubblex1")]
    [InlineData(6, "AlternateReadingx1,InferenceQuestionx1,SceneTitlex1,ThoughtBubblex1,CauseEffectx1")]
    [InlineData(12, "AlternateReadingx1,InferenceQuestionx1,SceneTitlex1,ThoughtBubblex1,CauseEffectx1")]
    public void Age8_PrioritizesFiveTypes(int blocks, string expected) => Assert.Equal(expected, DescribeAge8(blocks));

    [Fact]
    public void Age8_WithoutDialogue_AndOneSlot_AsksOneInferenceQuestion() =>
        Assert.Equal("InferenceQuestionx1", DescribeAge8(2, dialogue: false));

    [Fact]
    public void Age8_WithoutDialogue_AsksTwoInferenceQuestionsInsteadOfReadingAlong() =>
        Assert.Equal("InferenceQuestionx2,SceneTitlex1,ThoughtBubblex1,CauseEffectx1", DescribeAge8(6, dialogue: false));

    [Fact]
    public void Age8_UsesDistinctZones_ReadingAlongOnDialogue_ThoughtBubbleLatest()
    {
        var plan = ListeningInteractionPlanner.Create(8, Story(6));
        Assert.Equal(8, plan.TargetAge);
        var zones = plan.Items.SelectMany(x => x.Zones).ToList();
        Assert.Equal(5, zones.Count);
        Assert.Equal(5, zones.SelectMany(z => z).Distinct().Count());   // mỗi tương tác một vùng riêng
        var reading = plan.Items.Single(x => x.Type == T.AlternateReading).Zones.Single();
        Assert.Contains(reading[0], plan.DialogueBlockIds);
        var bubbleZone = plan.Items.Single(x => x.Type == T.ThoughtBubble).Zones.Single();
        Assert.Equal(zones.Max(z => z.Max(id => Story(6).First(b => b.BlockId == id).EndOffset)),
            bubbleZone.Max(id => Story(6).First(b => b.BlockId == id).EndOffset));
        Assert.DoesNotContain(plan.Items, x => ListeningInteractionRules.IsEndOfStory(x.Type, 8));
    }

    [Theory]
    [InlineData(2, "PictureSearchx1")]
    [InlineData(3, "PictureSearchx1,TrueFalsex1")]
    [InlineData(4, "PictureSearchx1,TrueFalsex1,SentenceBuilderx1")]
    [InlineData(5, "PictureSearchx1,TrueFalsex1,SentenceBuilderx1,FollowActionx1")]
    [InlineData(6, "PictureSearchx1,TrueFalsex1,SentenceBuilderx1,FollowActionx1,EventOrderingx1")]
    public void Age7_PrioritizesFiveTypes(int blocks, string expected)
    {
        var plan = ListeningInteractionPlanner.Create(7, Story(blocks));
        Assert.Equal(expected, string.Join(',', plan.Items.Select(item => $"{item.Type}x{item.Count}")));
        Assert.Equal(7, plan.TargetAge);
        Assert.All(plan.Items, item => Assert.Single(item.Zones));
        Assert.DoesNotContain(plan.Items.SelectMany(item => item.Zones).SelectMany(zone => zone),
            id => id == Story(blocks)[^1].BlockId);
    }
}
