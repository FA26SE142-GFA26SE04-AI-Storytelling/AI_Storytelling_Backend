using StoryPlatform.Application.Features.ListeningInteractions.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ListeningInteractions;

public sealed class ListeningAudioPartsTests
{
    [Fact]
    public void SentenceBuilder_ReadsCardsAndAcceptedSentences()
    {
        var interaction = new ListeningInteraction { Id = 9, Type = ListeningInteractionType.SentenceBuilder,
            TargetAge = 7, SourceQuote = "Bác Gấu dẫn Thỏ về hang.", AcceptedOrdersJson = "[[1,2,3],[2,1,3]]" };
        var options = new[] { "Bác Gấu", "dẫn Thỏ", "về hang" }.Select((text, index) =>
            new ListeningInteractionOption { Id = 90 + index, ListeningInteractionId = 9, Text = text,
                SortOrder = index, CorrectOrder = index + 1 }).ToArray();
        var parts = ListeningAudioParts.For(interaction, options);
        Assert.Equal(3, parts.Count(x => x.OptionId is not null && x.PartKey == ListeningAudioParts.Text));
        Assert.Equal("Bác Gấu dẫn Thỏ về hang.", parts.Single(x => x.PartKey == "sentence").Text);
        Assert.Equal("dẫn Thỏ Bác Gấu về hang", parts.Single(x => x.PartKey == "sentence:2").Text);
        Assert.Contains(ListeningAudioParts.Sentence, ListeningAudioParts.HighlightKeys);
    }

    [Fact]
    public void PictureSearch_ReadsPromptAndRetryHint()
    {
        var interaction = new ListeningInteraction { Id = 7, Type = ListeningInteractionType.PictureSearch,
            TargetAge = 7, Prompt = "Tìm bác Gấu", MatchFeedback = "Con tìm giỏi quá!" };
        var parts = ListeningAudioParts.For(interaction, []);
        Assert.Contains(parts, x => x.PartKey == ListeningAudioParts.Prompt);
        Assert.Contains(parts, x => x.PartKey == ListeningAudioParts.RetryLookClose && x.Text == "Nhìn kỹ lại nhé.");
    }

    [Fact]
    public void CharacterLetter_ReadsNameSoundsAndFixedGuidance()
    {
        var interaction = new ListeningInteraction { Id = 1, Type = ListeningInteractionType.CharacterLetter,
            TargetAge = 6, CharacterName = "Thỏ", BlankIndex = 0, MatchFeedback = "Giỏi quá!" };
        var options = new[] { ("th", true), ("b", false), ("m", false) }.Select((o, i) =>
            new ListeningInteractionOption { Id = 10 + i, ListeningInteractionId = 1, Text = o.Item1, IsMatch = o.Item2, SortOrder = i }).ToArray();
        var parts = ListeningAudioParts.For(interaction, options);
        Assert.Contains(parts, p => p.PartKey == ListeningAudioParts.Name && p.Text == "Thỏ");
        Assert.Contains(parts, p => p.OptionId == 10 && p.PartKey == ListeningAudioParts.Sound && p.Text == "thờ");
        Assert.Contains(parts, p => p.OptionId == 11 && p.PartKey == ListeningAudioParts.Sound && p.Text == "bờ");
        Assert.Contains(parts, p => p.PartKey == ListeningAudioParts.RetryArrow && p.Text == "Đi theo mũi tên nhé.");
        Assert.Contains(parts, p => p.PartKey == ListeningAudioParts.Unclear);
        Assert.All(parts, p => Assert.True(p.Required));
    }

    [Fact]
    public void WhoSaidIt_ReadsLineAndNames()
    {
        var interaction = new ListeningInteraction { Id = 2, Type = ListeningInteractionType.WhoSaidIt, TargetAge = 6,
            SourceQuote = "Có ai ở đó không?", MatchFeedback = "Là Thỏ!", NoMatchFeedback = "Mình nghe lại nhé." };
        var options = new[] { Option(20, 2, 0, "Thỏ"), Option(21, 2, 1, "bác Gấu") };
        Assert.Equal([":instruction", ":text", ":match", ":noMatch", "20:text", "21:text"],
            ListeningAudioParts.For(interaction, options).Select(p => $"{p.OptionId}:{p.PartKey}"));
    }

    [Fact]
    public void Literal_AddsFixedRetryLook() => Assert.Contains(
        ListeningAudioParts.For(new ListeningInteraction { Type = ListeningInteractionType.Checkpoint, Kind = CheckpointKind.Literal }, []),
        p => p.PartKey == ListeningAudioParts.RetryLook && p.Text == "Bé nhìn lại tranh nhé.");

    private static ListeningInteractionOption Option(int id, int interactionId, int order, string text,
        string? feedback = null, bool deleted = false) => new()
    {
        Id = id, ListeningInteractionId = interactionId, SortOrder = order, Text = text,
        Feedback = feedback, IsDeleted = deleted
    };
    private static string[] Keys(IReadOnlyList<ListeningAudioPart> parts) =>
        parts.Select(x => $"{(x.OptionId is int id ? $"o{id}" : "i")}:{x.PartKey}:{x.Text}").ToArray();

    [Fact]
    public void Checkpoint_ReadsPromptNoMatchAndOptionTextFeedback()
    {
        var interaction = new ListeningInteraction { Id = 1, Type = ListeningInteractionType.Checkpoint,
            Prompt = "Thỏ thấy sao?", NoMatchFeedback = "Ý hay đó." };
        Assert.Equal(new[] { "i:prompt:Thỏ thấy sao?", "i:noMatch:Ý hay đó.",
            "o10:text:Vui", "o10:feedback:Thỏ vui lắm.", "o11:text:Buồn", "o11:feedback:Thỏ không buồn." },
            Keys(ListeningAudioParts.For(interaction,
                [Option(11, 1, 2, "Buồn", "Thỏ không buồn."), Option(10, 1, 1, "Vui", "Thỏ vui lắm.")])));
    }

    private const string FillBlankInstruction = "i:instruction:Em hãy nghe câu rồi điền chữ còn thiếu vào chỗ trống nhé!";
    private const string OrderingInstruction = "i:instruction:Em hãy chạm vào các thẻ theo đúng thứ tự chuyện xảy ra trong truyện nhé!";

    [Fact]
    public void RolePlay_ReadsInstructionWithCharacterAndMatch() =>
        Assert.Equal(["i:instruction:Em hãy đóng vai Thỏ, nghe mẫu rồi nói theo câu này nhé!", "i:match:Giỏi quá!"], Keys(ListeningAudioParts.For(
            new ListeningInteraction { Id = 2, Type = ListeningInteractionType.RolePlay, CharacterName = " Thỏ ", MatchFeedback = "Giỏi quá!" }, [])));

    [Fact]
    public void RolePlay_WithoutCharacter_UsesGenericInstruction() =>
        Assert.Equal("Em hãy nghe mẫu rồi nói theo câu này nhé!",
            ListeningAudioParts.InstructionFor(new ListeningInteraction { Type = ListeningInteractionType.RolePlay }));

    [Theory]
    [InlineData(ListeningInteractionType.Checkpoint)]
    [InlineData(ListeningInteractionType.PicturePrediction)]
    [InlineData(ListeningInteractionType.ReadAloud)]
    public void TypesWithPromptOrNoChildCard_HaveNoInstruction(ListeningInteractionType type) =>
        Assert.Null(ListeningAudioParts.InstructionFor(new ListeningInteraction { Type = type, CharacterName = "Thỏ" }));

    [Fact]
    public void InstructionIsOptional_OtherPartsRequired()
    {
        var parts = ListeningAudioParts.For(new ListeningInteraction { Id = 2, Type = ListeningInteractionType.RolePlay,
            CharacterName = "Thỏ", MatchFeedback = "Giỏi quá!" }, []);
        Assert.False(parts.Single(x => x.PartKey == ListeningAudioParts.Instruction).Required);
        Assert.True(parts.Single(x => x.PartKey == ListeningAudioParts.Match).Required);
    }

    [Fact]
    public void FillBlank_ReadsShortSentenceHintAndFeedback_ButNotAcceptedAnswers() =>
        Assert.Equal([FillBlankInstruction, "i:text:Thỏ gặp thỏ mẹ.", "i:hint:Một con vật.", "i:match:Đúng rồi!", "i:noMatch:Thử nghe lại nhé."], Keys(
            ListeningAudioParts.For(new ListeningInteraction { Id = 3, Type = ListeningInteractionType.FillBlank,
                SourceQuote = "Thỏ gặp thỏ mẹ.", Hint = "Một con vật.", MatchFeedback = "Đúng rồi!", NoMatchFeedback = "Thử nghe lại nhé." },
                [Option(30, 3, 1, "thỏ")])));

    [Fact]
    public void EventOrdering_ReadsCardTextWithoutFeedback() =>
        Assert.Equal([OrderingInstruction, "i:match:Đúng rồi!", "i:noMatch:Xếp lại nhé.", "o40:text:Thỏ lạc đường"], Keys(
            ListeningAudioParts.For(new ListeningInteraction { Id = 4, Type = ListeningInteractionType.EventOrdering,
                MatchFeedback = "Đúng rồi!", NoMatchFeedback = "Xếp lại nhé." },
                [Option(40, 4, 1, "Thỏ lạc đường", "không đọc")])));

    [Fact]
    public void PicturePrediction_ReadsPromptWaitAndOptionTextFeedback() =>
        Assert.Equal(["i:prompt:Ai đến?", "i:wait:Nghe tiếp nhé!", "o50:text:Bác Gấu", "o50:feedback:Đúng là bác Gấu."], Keys(
            ListeningAudioParts.For(new ListeningInteraction { Id = 5, Type = ListeningInteractionType.PicturePrediction,
                Prompt = "Ai đến?", WaitFeedback = "Nghe tiếp nhé!" },
                [Option(50, 5, 1, "Bác Gấu", "Đúng là bác Gấu.")])));

    [Fact]
    public void SkipsBlankDeletedAndUnrelatedOptions() =>
        Assert.Equal(["i:prompt:Thỏ thấy sao?", "o10:text:Vui"], Keys(ListeningAudioParts.For(
            new ListeningInteraction { Id = 1, Type = ListeningInteractionType.Checkpoint,
                Prompt = "Thỏ thấy sao?", NoMatchFeedback = "  " },
            [Option(10, 1, 1, "Vui"), Option(11, 1, 2, "Buồn", "x", deleted: true),
             Option(99, 9, 1, "Khác", "y")])));
    [Fact]
    public void ReadAloud_ReadsPassageTextThenPraise() =>
        Assert.Equal(["i:text:Mẹ Thỏ ôm Thỏ vào lòng.", "i:match:Con đọc to rõ lắm!"], Keys(ListeningAudioParts.For(
            new ListeningInteraction { Id = 6, Type = ListeningInteractionType.ReadAloud,
                SourceQuote = "Mẹ Thỏ ôm Thỏ vào lòng.", MatchFeedback = "Con đọc to rõ lắm!" }, [])));


    // ---- Đúng/sai: nói đáp án khi sai lần 2 ----
    [Fact]
    public void TrueFalse_ReadsBothStatementsAndTheFixedTruthLines()
    {
        var interaction = new ListeningInteraction { Id = 6, Type = ListeningInteractionType.TrueFalse, TargetAge = 7,
            MatchFeedback = "Con nghe kỹ quá!", NoMatchFeedback = "Mình nghe lại nhé." };
        var options = new[]
        {
            new ListeningInteractionOption { Id = 60, ListeningInteractionId = 6, SortOrder = 1, Text = "Thỏ nghe tiếng động", IsMatch = true },
            new ListeningInteractionOption { Id = 61, ListeningInteractionId = 6, SortOrder = 2, Text = "Thỏ ngủ trong hang" }
        };
        var parts = ListeningAudioParts.For(interaction, options);
        Assert.Equal(["60:text", "61:text"], parts.Where(p => p.OptionId is not null).Select(p => $"{p.OptionId}:{p.PartKey}"));
        Assert.Equal(ListeningAudioParts.FixedLine(ListeningAudioParts.TruthYes), parts.Single(p => p.PartKey == ListeningAudioParts.TruthYes).Text);
        Assert.Equal(ListeningAudioParts.FixedLine(ListeningAudioParts.TruthNo), parts.Single(p => p.PartKey == ListeningAudioParts.TruthNo).Text);
        Assert.All(parts.Where(p => p.PartKey is ListeningAudioParts.TruthYes or ListeningAudioParts.TruthNo), p => Assert.True(p.Required));
    }

    [Theory]
    [InlineData(ListeningAudioParts.TruthYes)]
    [InlineData(ListeningAudioParts.TruthNo)]
    [InlineData(ListeningAudioParts.RetryLookClose)]
    [InlineData(ListeningAudioParts.RetryHear)]
    [InlineData(ListeningAudioParts.RetryTry)]
    public void FixedLines_NeverContainGradingWords_AndFitTheAudioKeyColumn(string key)
    {
        Assert.False(ListeningInteractionRules.ContainsGradingWord(ListeningAudioParts.FixedLine(key), ListeningInteractionRules.DefaultGradingWords));
        Assert.True(key.Length <= 30);              // MediaAsset.AudioPartKey HasMaxLength(30)
    }

    [Theory]
    [InlineData(0, "sentence")]
    [InlineData(1, "sentence:2")]
    [InlineData(5, "sentence:6")]
    public void SentenceKey_NumbersTheAcceptedOrders(int index, string expected)
    {
        Assert.Equal(expected, ListeningAudioParts.SentenceKey(index));
        Assert.True(ListeningAudioParts.SentenceKey(index).Length <= 30);
    }

    [Fact]
    public void FollowAction_ReadsPromptCardsAndFeedback_AndNeedsPicturesForEveryCard()
    {
        var interaction = new ListeningInteraction { Id = 8, Type = ListeningInteractionType.FollowAction, TargetAge = 7,
            Prompt = "Bé giúp Thỏ nhé: dắt bác Gấu về cửa hang", MatchFeedback = "Con kéo giỏi quá!", NoMatchFeedback = "Bé thử lại nhé." };
        var options = new[]
        {
            new ListeningInteractionOption { Id = 80, ListeningInteractionId = 8, SortOrder = 1, Text = "bác Gấu", Role = ListeningOptionRole.Item, IsMatch = true },
            new ListeningInteractionOption { Id = 81, ListeningInteractionId = 8, SortOrder = 2, Text = "cửa hang", Role = ListeningOptionRole.Target }
        };
        var keys = ListeningAudioParts.For(interaction, options).Select(p => $"{p.OptionId?.ToString() ?? "i"}:{p.PartKey}").ToArray();
        Assert.Contains("i:prompt", keys);
        Assert.Contains("80:text", keys);
        Assert.Contains("81:text", keys);
        Assert.Contains("i:retryTry", keys);
        Assert.True(ListeningMediaRequirements.HasOptionImages(interaction));
    }

    [Fact]
    public void HighlightKeys_CoverTheSentenceButNotFeedback()
    {
        Assert.Contains(ListeningAudioParts.Sentence, ListeningAudioParts.HighlightKeys);
        Assert.DoesNotContain(ListeningAudioParts.Match, ListeningAudioParts.HighlightKeys);
        Assert.DoesNotContain(ListeningAudioParts.NoMatch, ListeningAudioParts.HighlightKeys);
    }

    [Theory]
    [InlineData(ListeningInteractionType.PictureSearch)]
    [InlineData(ListeningInteractionType.TrueFalse)]
    [InlineData(ListeningInteractionType.FollowAction)]
    [InlineData(ListeningInteractionType.SentenceBuilder)]
    public void Age7Types_HaveARequiredSpokenInstruction(ListeningInteractionType type)
    {
        var interaction = new ListeningInteraction { Id = 1, Type = type, TargetAge = 7 };
        Assert.False(string.IsNullOrWhiteSpace(ListeningAudioParts.InstructionFor(interaction)));
        Assert.True(ListeningAudioParts.For(interaction, []).Single(p => p.PartKey == ListeningAudioParts.Instruction).Required);
    }

}
