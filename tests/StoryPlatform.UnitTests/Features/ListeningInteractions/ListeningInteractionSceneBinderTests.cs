using StoryPlatform.Application.Features.ListeningInteractions.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ListeningInteractions;

public sealed class ListeningInteractionSceneBinderTests
{
    private static readonly IReadOnlyList<StoryScene> Scenes =
    [
        new() { Id = 100, SceneIndex = 0, TextRangeStart = 0, TextRangeEnd = 3 },
        new() { Id = 101, SceneIndex = 1, TextRangeStart = 3, TextRangeEnd = 6 },
        new() { Id = 102, SceneIndex = 2, TextRangeStart = 6, TextRangeEnd = 7 }
    ];

    private static ListeningInteraction Interaction(int id, ListeningInteractionType type, int anchor, bool deleted = false) =>
        new() { Id = id, StoryVersionId = 10, Type = type, AnchorOffset = anchor, IsDeleted = deleted };

    [Fact]
    public void BindsToSceneEndingAtAnchor()
    {
        var bound = ListeningInteractionSceneBinder.Bind(Scenes,
            [Interaction(1, ListeningInteractionType.Checkpoint, 3),
             Interaction(2, ListeningInteractionType.PicturePrediction, 6)]);
        Assert.Equal(100, bound[1]);
        Assert.Equal(101, bound[2]);
    }

    [Fact]
    public void EventOrderingAtStoryEnd_BindsToLastScene() =>
        Assert.Equal(102, ListeningInteractionSceneBinder.Bind(Scenes,
            [Interaction(3, ListeningInteractionType.EventOrdering, 7)])[3]);

    [Fact]
    public void DeletedInteractions_AreIgnored() =>
        Assert.Empty(ListeningInteractionSceneBinder.Bind(Scenes,
            [Interaction(4, ListeningInteractionType.Checkpoint, 1, deleted: true)]));

    [Fact]
    public void AnchorInsideScene_Throws() =>
        Assert.Equal("INTERACTION_SCENE_UNRESOLVED", Assert.Throws<InvalidOperationException>(() =>
            ListeningInteractionSceneBinder.Bind(Scenes,
                [Interaction(5, ListeningInteractionType.Checkpoint, 4)])).Message);
}
