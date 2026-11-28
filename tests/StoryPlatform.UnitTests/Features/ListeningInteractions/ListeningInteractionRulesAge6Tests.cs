using StoryPlatform.Application.Features.ListeningInteractions.Models;
using StoryPlatform.Application.Features.ListeningInteractions.Services;
using StoryPlatform.Domain.Enums;
using Xunit;
using S = StoryPlatform.UnitTests.Features.ListeningInteractions.ListeningTestStory;
using T = StoryPlatform.Domain.Enums.ListeningInteractionType;

namespace StoryPlatform.UnitTests.Features.ListeningInteractions;

public sealed class ListeningInteractionRulesAge6Tests
{
    private static ListeningRuleContext Ctx(T type, int count = 1) => S.Context(type, count) with { TargetAge = 6 };

    private static string[] Codes(ListeningInteractionDraft item, ListeningRuleContext ctx)
    {
        var mapped = ListeningInteractionMapper.Resolve([item], S.Content, S.Blocks);
        return mapped.Errors.Concat(ListeningInteractionRules.Validate(mapped.Items, ctx)).Select(e => e.Code).Distinct().ToArray();
    }

    private static ListeningInteractionDraft Letter(string name = "Thỏ", int blank = 0, params (string Text, bool Match)[] options) => new()
    {
        Type = T.CharacterLetter, AnchorBlockId = "P1", CharacterName = name, BlankIndex = blank,
        MatchFeedback = "Bé giỏi quá!",
        Options = (options.Length > 0 ? options : [("th", true), ("b", false), ("m", false)])
            .Select((o, i) => new ListeningOptionDraft { Text = o.Text, IsMatch = o.Match, ImageDescription = o.Match ? "White rabbit" : null }).ToArray()
    };

    [Fact]
    public void CharacterLetter_Valid() => Assert.Empty(Codes(Letter(), Ctx(T.CharacterLetter)));

    [Fact]
    public void CharacterLetter_BlankOnToneMarkedVowel_Rejected() =>
        Assert.Contains("LETTER_BLANK_INVALID", Codes(Letter(blank: 1, options: [("o", true), ("a", false), ("e", false)]), Ctx(T.CharacterLetter)));

    [Fact]
    public void CharacterLetter_MatchNotTheBlankedUnit_Rejected() =>
        Assert.Contains("LETTER_BLANK_INVALID", Codes(Letter(options: [("th", false), ("b", true), ("m", false)]), Ctx(T.CharacterLetter)));

    [Fact]
    public void CharacterLetter_NeedsExactlyThreeOptions() =>
        Assert.Contains("OPTION_COUNT_INVALID", Codes(Letter(options: [("th", true), ("b", false)]), Ctx(T.CharacterLetter)));

    [Fact]
    public void CharacterLetter_NameNotInStory_Rejected() =>
        Assert.Contains("NOT_GROUNDED", Codes(Letter(name: "Mèo"), Ctx(T.CharacterLetter)));

    // Neo P3: câu thoại ở P2, "bác Gấu" xuất hiện ở P3 — cả hai đều trước neo.
    private static ListeningInteractionDraft WhoSaid(string line = "Có ai ở đó không?", string anchor = "P3") => new()
    {
        Type = T.WhoSaidIt, AnchorBlockId = anchor, SourceQuote = line,
        MatchFeedback = "Đúng là bạn Thỏ rồi!", NoMatchFeedback = "Mình nghe lại nhé.",
        Options =
        [
            new ListeningOptionDraft { Text = "Thỏ", IsMatch = true, ImageDescription = "Chú thỏ trắng nhỏ" },
            new ListeningOptionDraft { Text = "bác Gấu", ImageDescription = "Bác gấu nâu to" }
        ]
    };

    [Fact]
    public void WhoSaidIt_Valid() => Assert.Empty(Codes(WhoSaid(), Ctx(T.WhoSaidIt)));

    [Fact]
    public void WhoSaidIt_LineNotDialogue_Rejected() =>
        Assert.Contains("SPEAKER_INVALID", Codes(WhoSaid(line: "Trời bắt đầu tối."), Ctx(T.WhoSaidIt)));

    [Fact]
    public void WhoSaidIt_MissingImage_Rejected()
    {
        var item = WhoSaid() with { Options = [WhoSaid().Options[0] with { ImageDescription = null }, WhoSaid().Options[1]] };
        Assert.Contains("SCHEMA_INVALID", Codes(item, Ctx(T.WhoSaidIt)));
    }

    private static ListeningInteractionDraft LiteralItem(string? firstPicture, string? secondPicture) => new()
    {
        Type = T.Checkpoint, Kind = CheckpointKind.Literal, AnchorBlockId = "P1", SourceQuote = S.Block("P1").Text.TrimEnd(), Prompt = "Ai đi lạc vào rừng?",
        NoMatchFeedback = "Bé nhìn lại tranh nhé.",
        Options =
        [
            new ListeningOptionDraft { Text = "Thỏ con", IsMatch = true, Feedback = "Đúng là Thỏ con!", ImageDescription = firstPicture },
            new ListeningOptionDraft { Text = "bác Gấu", Feedback = "Mình xem lại nhé.", ImageDescription = secondPicture }
        ]
    };

    [Fact]
    public void Literal_TwoOptionsOneMatch_Valid() =>
        Assert.Empty(Codes(LiteralItem("Thỏ con đứng trước rừng", "Bác Gấu nâu to"), Ctx(T.Checkpoint)));

    [Fact]
    public void Literal_OptionWithoutPicture_Rejected() =>
        Assert.Contains("SCHEMA_INVALID", Codes(LiteralItem("Thỏ con đứng trước rừng", null), Ctx(T.Checkpoint)));

    [Fact]
    public void Literal_AllOptionsWithoutPicture_Rejected() =>
        Assert.Contains("SCHEMA_INVALID", Codes(LiteralItem(null, null), Ctx(T.Checkpoint)));

    [Fact]
    public void Picture_Age6_NoMatchAllowed()
    {
        var item = new ListeningInteractionDraft
        {
            // Câu hỏi phải có từ bám truyện ("Thỏ") để qua NOT_GROUNDED.
            Type = T.PicturePrediction, AnchorBlockId = "P1", SourceQuote = S.Block("P1").Text.TrimEnd(), Prompt = "Trong r\u1eebng, theo bé, Thỏ sẽ gặp chuyện gì tiếp?",
            WaitFeedback = "Mình cùng nghe tiếp nhé!",
            Options =
            [
                new ListeningOptionDraft { Text = "Thỏ gặp bạn", ImageDescription = "Thỏ gặp gấu", Feedback = "Hay quá!" },
                new ListeningOptionDraft { Text = "Thỏ ngủ", ImageDescription = "Thỏ ngủ dưới gốc cây", Feedback = "Hay quá!" }
            ]
        };
        Assert.Empty(Codes(item, Ctx(T.PicturePrediction)));
        Assert.Contains("MATCH_RULE_INVALID", Codes(item with { Options = [item.Options[0] with { IsMatch = true }, item.Options[1]] }, Ctx(T.PicturePrediction)));
        Assert.Contains("OPTION_COUNT_INVALID", Codes(item with { Options = [.. item.Options, item.Options[0] with { Text = "Thỏ chạy" }] }, Ctx(T.PicturePrediction)));
    }
}
