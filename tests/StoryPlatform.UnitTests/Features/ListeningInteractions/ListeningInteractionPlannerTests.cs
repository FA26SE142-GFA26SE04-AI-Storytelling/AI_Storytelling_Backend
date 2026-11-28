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
    [InlineData(7)]
    [InlineData(9)]
    public void UnbuiltAge_HasEmptyPlan(int age) =>
        Assert.Empty(ListeningInteractionPlanner.Create(age, Story(6)).Items);
}
