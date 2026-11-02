using StoryPlatform.Application.Features.ListeningInteractions.Services;
using StoryPlatform.Application.Features.MediaGeneration.Models;
using StoryPlatform.Application.Features.MediaGeneration.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ListeningInteractions;

public sealed class InteractionSceneBoundaryEnforcerTests
{
    private static readonly IReadOnlyList<StoryTextBlock> Blocks =
        new StoryBlockParser().Parse("A\n\nB\n\nC\n\nD\n\nE\n\nF");
    private static readonly IReadOnlyList<SceneSelection> TwoScenes =
    [
        new(0, ["P1", "P2", "P3"], "opening"),
        new(1, ["P4", "P5", "P6"], "ending")
    ];

    private static StoryTextBlock Block(string id) => Blocks.Single(x => x.BlockId == id);
    private static ListeningInteraction Interaction(int id, ListeningInteractionType type, string anchor,
        string? reveal = null, bool deleted = false) => new()
    {
        Id = id, StoryVersionId = 10, Type = type,
        AnchorOffset = Block(anchor).EndOffset,
        RevealOffset = reveal is null ? null : Block(reveal).EndOffset,
        IsDeleted = deleted
    };
    private static ListeningInteractionOption Event(int interactionId, string block) => new()
    {
        ListeningInteractionId = interactionId, Text = "Sự kiện", SourceStart = Block(block).StartOffset
    };
    private static string[] Shape(IReadOnlyList<SceneSelection> scenes) =>
        scenes.Select(x => $"{x.SceneIndex}:{string.Join(",", x.BlockIds)}:{x.Focus}").ToArray();

    [Fact]
    public void NoInteractions_ReturnsInputUnchanged() =>
        Assert.Same(TwoScenes, InteractionSceneBoundaryEnforcer.Enforce(Blocks, TwoScenes, [], []));

    [Fact]
    public void OnlyDeletedInteractions_ReturnsInputUnchanged() =>
        Assert.Same(TwoScenes, InteractionSceneBoundaryEnforcer.Enforce(Blocks, TwoScenes,
            [Interaction(1, ListeningInteractionType.Checkpoint, "P2", deleted: true)], []));

    [Fact]
    public void AnchorAlreadyAtSceneEnd_DoesNotSplit() =>
        Assert.Equal(["0:P1,P2,P3:opening", "1:P4,P5,P6:ending"], Shape(
            InteractionSceneBoundaryEnforcer.Enforce(Blocks, TwoScenes,
                [Interaction(1, ListeningInteractionType.Checkpoint, "P3")], [])));

    [Fact]
    public void AnchorInsideScene_SplitsAfterAnchorAndPreservesFocus() =>
        Assert.Equal(["0:P1,P2:opening", "1:P3:opening", "2:P4,P5,P6:ending"], Shape(
            InteractionSceneBoundaryEnforcer.Enforce(Blocks, TwoScenes,
                [Interaction(1, ListeningInteractionType.Checkpoint, "P2")], [])));

    [Fact]
    public void MultipleAnchors_SplitAndRenumber() =>
        Assert.Equal(["0:P1:opening", "1:P2:opening", "2:P3:opening", "3:P4,P5,P6:ending"], Shape(
            InteractionSceneBoundaryEnforcer.Enforce(Blocks, TwoScenes,
                [Interaction(1, ListeningInteractionType.Checkpoint, "P1"),
                 Interaction(2, ListeningInteractionType.RolePlay, "P2")], [])));

    [Fact]
    public void PicturePrediction_SplitsAtAnchorAndReveal() =>
        Assert.Equal(["0:P1:opening", "1:P2,P3:opening", "2:P4,P5:ending", "3:P6:ending"], Shape(
            InteractionSceneBoundaryEnforcer.Enforce(Blocks, TwoScenes,
                [Interaction(1, ListeningInteractionType.PicturePrediction, "P1", "P5")], [])));

    [Fact]
    public void EventOrdering_SplitsAtEachEventBlock() =>
        Assert.Equal(["0:P1:opening", "1:P2,P3:opening", "2:P4,P5:ending", "3:P6:ending"], Shape(
            InteractionSceneBoundaryEnforcer.Enforce(Blocks, TwoScenes,
                [Interaction(1, ListeningInteractionType.EventOrdering, "P6")],
                [Event(1, "P1"), Event(1, "P3"), Event(1, "P5")])));

    [Fact]
    public void EventOrdering_IgnoresOtherAndDeletedRows()
    {
        var deletedEvent = Event(1, "P4");
        deletedEvent.IsDeleted = true;
        Assert.Equal(["0:P1,P2,P3:opening", "1:P4,P5,P6:ending"], Shape(
            InteractionSceneBoundaryEnforcer.Enforce(Blocks, TwoScenes,
                [Interaction(1, ListeningInteractionType.EventOrdering, "P6"),
                 Interaction(2, ListeningInteractionType.Checkpoint, "P2", deleted: true)],
                [Event(1, "P3"), deletedEvent, Event(99, "P1")])));
    }

    [Fact]
    public void AnchorNotAtBlockEnd_Throws()
    {
        var interaction = Interaction(1, ListeningInteractionType.Checkpoint, "P2");
        interaction.AnchorOffset--;
        Assert.Equal("INTERACTION_BOUNDARY_UNRESOLVED", Assert.Throws<InvalidOperationException>(() =>
            InteractionSceneBoundaryEnforcer.Enforce(Blocks, TwoScenes, [interaction], [])).Message);
    }

    [Fact]
    public void PicturePredictionWithoutReveal_Throws() =>
        Assert.Equal("INTERACTION_BOUNDARY_UNRESOLVED", Assert.Throws<InvalidOperationException>(() =>
            InteractionSceneBoundaryEnforcer.Enforce(Blocks, TwoScenes,
                [Interaction(1, ListeningInteractionType.PicturePrediction, "P2")], [])).Message);

    [Fact]
    public void EventWithoutSourceStart_Throws()
    {
        var option = Event(1, "P1");
        option.SourceStart = null;
        Assert.Equal("INTERACTION_BOUNDARY_UNRESOLVED", Assert.Throws<InvalidOperationException>(() =>
            InteractionSceneBoundaryEnforcer.Enforce(Blocks, TwoScenes,
                [Interaction(1, ListeningInteractionType.EventOrdering, "P6")], [option])).Message);
    }

    [Fact]
    public void NonSequentialSceneIndex_Throws()
    {
        IReadOnlyList<SceneSelection> gapped = [new(0, ["P1", "P2", "P3"]), new(2, ["P4", "P5", "P6"])];
        Assert.Equal("INVALID_SCENE_ORDER", Assert.Throws<InvalidOperationException>(() =>
            InteractionSceneBoundaryEnforcer.Enforce(Blocks, gapped,
                [Interaction(1, ListeningInteractionType.Checkpoint, "P2")], [])).Message);
    }

    [Fact]
    public void EmptyScene_Throws()
    {
        IReadOnlyList<SceneSelection> withEmpty = [new(0, ["P1", "P2", "P3", "P4", "P5", "P6"]), new(1, [])];
        Assert.Equal("INVALID_SCENE_ORDER", Assert.Throws<InvalidOperationException>(() =>
            InteractionSceneBoundaryEnforcer.Enforce(Blocks, withEmpty,
                [Interaction(1, ListeningInteractionType.Checkpoint, "P2")], [])).Message);
    }
}
