using StoryPlatform.Application.Features.ListeningInteractions.Services;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ListeningInteractions;

public sealed class SentenceOrdersTests
{
    [Fact] public void Parse_ReadsArrays() => Assert.Equal(2, SentenceOrders.Parse("[[1,2,3],[2,1,3]]")!.Count);
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("[1,2,3]")]
    [InlineData("nope")]
    public void Parse_Bad_ReturnsNull(string? json) => Assert.Null(SentenceOrders.Parse(json));

    [Fact] public void IsValid_AcceptsPermutations() => Assert.True(SentenceOrders.IsValid([[1, 2, 3], [2, 1, 3]], 3));
    [Fact] public void IsValid_RejectsDuplicateOrders() => Assert.False(SentenceOrders.IsValid([[1, 2, 3], [1, 2, 3]], 3));
    [Fact] public void IsValid_RejectsNonPermutation() => Assert.False(SentenceOrders.IsValid([[1, 2, 2]], 3));
    [Fact] public void IsValid_RejectsEmpty() => Assert.False(SentenceOrders.IsValid([], 3));
    [Fact] public void IsValid_RejectsNull() => Assert.False(SentenceOrders.IsValid(null, 3));
    [Fact] public void IsValid_RejectsWrongCardCount() => Assert.False(SentenceOrders.IsValid([[1, 2, 3]], 4));
    [Theory]
    [InlineData(2)]
    [InlineData(6)]
    public void IsValid_OnlyThreeToFiveCards(int cards) =>
        Assert.False(SentenceOrders.IsValid([Enumerable.Range(1, cards).ToArray()], cards));

    [Fact]
    public void IsValid_RejectsMoreThanSixOrders()
    {
        var all = new[] { new[] { 1, 2, 3 }, new[] { 1, 3, 2 }, new[] { 2, 1, 3 }, new[] { 2, 3, 1 }, new[] { 3, 1, 2 }, new[] { 3, 2, 1 } };
        Assert.True(SentenceOrders.IsValid(all, 3));            // đúng 6 cách
        var four = new[] { new[] { 1, 2, 3, 4 }, new[] { 1, 2, 4, 3 }, new[] { 1, 3, 2, 4 }, new[] { 1, 3, 4, 2 },
            new[] { 1, 4, 2, 3 }, new[] { 1, 4, 3, 2 }, new[] { 2, 1, 3, 4 } };
        Assert.False(SentenceOrders.IsValid(four, 4));          // 7 cách
    }

    [Fact]
    public void Matches_AnyAcceptedOrder()
    {
        Assert.True(SentenceOrders.Matches([[1, 2, 3], [2, 1, 3]], [2, 1, 3]));
        Assert.False(SentenceOrders.Matches([[1, 2, 3]], [3, 2, 1]));
    }

    [Fact] public void Canonical_IsTheFirstOrder() => Assert.Equal([1, 2, 3], SentenceOrders.Canonical([[1, 2, 3], [2, 1, 3]]));
}
