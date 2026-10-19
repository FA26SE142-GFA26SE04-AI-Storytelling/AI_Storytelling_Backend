using StoryPlatform.Application.Features.Moderation;
using Xunit;

namespace StoryPlatform.UnitTests.Features.Moderation;

public class TextDiffTests
{
    private static string[] Types(IEnumerable<TextDiffEntry> entries) => entries.Select(entry => entry.Type).ToArray();

    [Fact]
    public void SplitUnits_SplitsParagraphsThenSentences()
    {
        Assert.Equal(new[] { "Một.", "Hai!", "Ba?", "Bốn…", "Năm" },
            TextDiff.SplitUnits("Một. Hai! Ba?\n\n  Bốn…  Năm  "));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n  ")]
    public void SplitUnits_EmptyText_ReturnsNothing(string? text) => Assert.Empty(TextDiff.SplitUnits(text));

    [Fact]
    public void Compare_HandlesUnchangedAddedRemovedAndModifiedSentences()
    {
        Assert.Equal(new[] { "unchanged", "unchanged", "unchanged" }, Types(TextDiff.Compare("A. B. C.", "A. B. C.")));
        Assert.Equal(new[] { "unchanged", "added", "unchanged" }, Types(TextDiff.Compare("A. C.", "A. B. C.")));
        Assert.Equal(new[] { "unchanged", "removed", "unchanged" }, Types(TextDiff.Compare("A. B. C.", "A. C.")));

        var modified = TextDiff.Compare("Mở đầu. Giữa truyện. Kết thúc.", "Mở đầu. Giữa truyện đã đổi. Kết thúc.");
        Assert.Equal(new[] { "unchanged", "modified", "unchanged" }, Types(modified));
        Assert.Equal("Giữa truyện.", modified[1].Before);
        Assert.Equal("Giữa truyện đã đổi.", modified[1].After);
    }

    [Fact]
    public void Compare_EmptySides_ReturnsOnlyAddedOrRemoved()
    {
        Assert.Equal(new[] { "added", "added" }, Types(TextDiff.Compare(null, "A. B.")));
        Assert.Equal(new[] { "removed", "removed" }, Types(TextDiff.Compare("A. B.", "")));
    }
}
