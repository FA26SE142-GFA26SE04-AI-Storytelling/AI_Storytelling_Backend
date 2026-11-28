using StoryPlatform.Application.Features.ListeningInteractions.Services;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ListeningInteractions;

public sealed class Age9PureFunctionsTests
{
    [Fact]
    public void Splitter_keeps_quoted_speech_together() =>
        Assert.Equal(["Thỏ hỏi: \"Có ai ở đó không?\"", "Bác Gấu đáp: \"Bác đây!\""],
            SentenceSplitter.Split("Thỏ hỏi: \"Có ai ở đó không?\" Bác Gấu đáp: \"Bác đây!\""));

    [Fact]
    public void Splitter_discards_empty_parts_and_keeps_punctuation() =>
        Assert.Equal(["Một câu...", "Câu nữa!", "Cuối cùng?"],
            SentenceSplitter.Split("  Một câu...  Câu nữa! Cuối cùng?  "));

    [Fact]
    public void Editor_requires_all_main_sentences_within_limit()
    {
        Assert.True(EditorSentences.Grade([true, false, true, false, false], new HashSet<int> { 0, 2 }).Matched);
        Assert.Equal([2], EditorSentences.Grade([true, false, true, false, false], new HashSet<int> { 0, 1 }).MissingMain);
        Assert.True(EditorSentences.Grade([true, false, true, false, false], new HashSet<int> { 0, 1, 2, 3 }).TooLong);
        Assert.Equal([0, 2], EditorSentences.Model([true, false, true, false, false]));
    }

    [Fact]
    public void Relation_map_accepts_any_allowed_card_and_rejects_invalid_edges()
    {
        var map = RelationMap.TryParse("""{"edges":[{"from":0,"to":1,"accepted":[0,2],"evidence":"a","explanation":"b"},{"from":1,"to":2,"accepted":[1],"evidence":"c","explanation":"d"},{"from":0,"to":2,"accepted":[3],"evidence":"e","explanation":"f"}]}""")!;
        Assert.True(map.IsValid(3, 4));
        Assert.Equal([true, false, false], map.Grade(new Dictionary<int, int> { [0] = 2, [1] = 0, [2] = 1 }));
        Assert.False(map.IsValid(2, 4));
        Assert.Null(RelationMap.TryParse("{bad json"));
        Assert.False((map with { Edges = [map.Edges[0] with { To = 0 }, map.Edges[1], map.Edges[2]] }).IsValid(3, 4));
    }
}
