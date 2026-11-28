using StoryPlatform.Application.Features.ListeningInteractions.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ListeningInteractions;

public sealed class CausePairsAndClueSpansTests
{
    private static (ListeningOptionRole, int?) C(ListeningOptionRole r, int p) => (r, p);
    private const ListeningOptionRole I = ListeningOptionRole.Item, T = ListeningOptionRole.Target;

    [Fact]
    public void ThreeItemsAndThreeTargets_WithMatchingPairNumbers_AreValid() =>
        Assert.True(CausePairs.IsValid([C(I, 1), C(I, 2), C(I, 3), C(T, 3), C(T, 1), C(T, 2)]));

    [Fact]
    public void DuplicatedPairNumberOrWrongCounts_AreInvalid()
    {
        Assert.False(CausePairs.IsValid([C(I, 1), C(I, 1), C(I, 3), C(T, 1), C(T, 2), C(T, 3)]));
        Assert.False(CausePairs.IsValid([C(I, 1), C(T, 1)]));
        Assert.False(CausePairs.IsValid([C(I, 1), C(I, 2), C(I, 3), C(I, 1), C(T, 2), C(T, 3)]));
    }

    [Fact]
    public void Matches_RequiresItemAndTargetWithTheSamePair()
    {
        var effect = new ListeningInteractionOption { Role = I, CorrectOrder = 2 };
        Assert.True(CausePairs.Matches(effect, new ListeningInteractionOption { Role = T, CorrectOrder = 2 }));
        Assert.False(CausePairs.Matches(effect, new ListeningInteractionOption { Role = T, CorrectOrder = 3 }));
        Assert.False(CausePairs.Matches(effect, new ListeningInteractionOption { Role = I, CorrectOrder = 2 }));
    }

    [Fact]
    public void Sentence_JoinsCauseAndEffectInVietnamese() =>
        Assert.Equal("Vì trời mưa to nên thỏ trú dưới gốc cây.", CausePairs.Sentence("Trời mưa to.", "Thỏ trú dưới gốc cây"));

    [Fact]
    public void ClueSpans_FindsEachClueInOrderOfAppearance()
    {
        const string passage = "Thỏ run lẩy bẩy. Thỏ nghe tiếng sói hú. Thỏ nấp sau cây.";
        var spans = ClueSpans.Find(passage, ["nghe tiếng sói hú", "run lẩy bẩy"]);
        Assert.NotNull(spans);
        Assert.Equal(passage.IndexOf("run lẩy bẩy", StringComparison.Ordinal), spans[0].Start);
        Assert.Equal(passage.IndexOf("nghe tiếng sói hú", StringComparison.Ordinal) + "nghe tiếng sói hú".Length, spans[1].End);
    }

    [Fact]
    public void ClueSpans_MissingDuplicatedOrOverlapping_ReturnsNull()
    {
        Assert.Null(ClueSpans.Find("Thỏ run lẩy bẩy.", ["con sói"]));
        Assert.Null(ClueSpans.Find("Thỏ run lẩy bẩy.", ["run", "run"]));
        Assert.Null(ClueSpans.Find("Thỏ run lẩy bẩy.", ["run lẩy", "lẩy bẩy"]));
        Assert.Null(ClueSpans.Find("Thỏ run.", [" "]));
    }
}
