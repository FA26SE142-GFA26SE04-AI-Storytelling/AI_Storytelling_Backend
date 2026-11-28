using System.Text.Json;
using StoryPlatform.Application.Features.ListeningInteractions.Models;
using StoryPlatform.Application.Features.ListeningInteractions.Services;
using StoryPlatform.Application.Features.MediaGeneration.Models;
using StoryPlatform.Application.Features.MediaGeneration.Services;
using StoryPlatform.Domain.Enums;
using Xunit;
using T = StoryPlatform.Domain.Enums.ListeningInteractionType;

namespace StoryPlatform.UnitTests.Features.ListeningInteractions;

public sealed class ListeningInteractionRulesAge9Tests
{
    private const string Content =
        "Mai, Nam và An cùng chăm sóc khu vườn. Mai đặt bình nước cạnh luống hoa. Nam mang hạt giống tới.\n\n" +
        "Nam giúp Mai gieo hạt. An che nắng cho Nam bằng chiếc ô. Mai tin tưởng An và nhờ bạn trông luống hoa.\n\n" +
        "Buổi sáng, ba bạn ra vườn. Mai tưới luống hoa. Nam nhặt lá khô. An đặt ô dưới gốc cây. Một con bướm bay qua. Cả ba cùng chăm sóc khu vườn.\n\n" +
        "Trời đổ mưa, Mai muốn bảo vệ luống hoa. Mai quyết định ở lại che luống hoa.\n\n" +
        "Nam quên hạt giống. Mai quyết định quay lại lấy giúp bạn.\n\n" +
        "Cuối cùng cả ba cùng vui.";

    private static readonly IReadOnlyList<StoryTextBlock> Blocks = new StoryBlockParser().Parse(Content);

    private static ListeningRuleContext Ctx(T type) => new()
    {
        Type = type, Count = 1, AgeBand = AgeBand.Age_9_10, TargetAge = 9, Content = Content, Blocks = Blocks,
        DialogueBlockIds = [], GroundingSource = Content
    };

    private static ListeningOptionDraft Opt(string text, bool match = false, ListeningOptionRole role = ListeningOptionRole.None) =>
        new() { Text = text, IsMatch = match, Role = role };

    private static string Relations(string firstEvidence = "Nam giúp Mai gieo hạt.", string misunderstandingEvidence = "Mai tin tưởng An và nhờ bạn trông luống hoa.") =>
        JsonSerializer.Serialize(new
        {
            edges = new[]
            {
                new { from = 1, to = 0, accepted = new[] { 0 }, evidence = firstEvidence, explanation = "Nam giúp Mai gieo hạt trong vườn." },
                new { from = 2, to = 1, accepted = new[] { 1 }, evidence = "An che nắng cho Nam bằng chiếc ô.", explanation = "An dùng ô để bảo vệ Nam khỏi nắng." },
                new { from = 0, to = 2, accepted = new[] { 2 }, evidence = misunderstandingEvidence, explanation = "Mai tin An nên nhờ bạn trông hoa." }
            }
        });

    private static ListeningInteractionDraft Base(T type) => type switch
    {
        T.FindWrongDetail => new()
        {
            Type = type, TargetAge = 9, AnchorBlockId = "P1",
            SourceQuote = "Mai, Nam và An cùng chăm sóc khu vườn. Mai đặt bình nước cạnh luống hoa. Nam mang hạt giống tới.",
            Prompt = "Mai, Nam và An cùng chăm sóc khu vườn. Mai đặt chiếc ô cạnh luống hoa. Nam mang hạt giống tới.",
            HiddenWord = "chiếc ô", Explanation = "Trong truyện, Mai đặt bình nước cạnh luống hoa.", MatchFeedback = "Con tìm ra rồi!",
            Options = [Opt("bình nước", true), Opt("cái xô"), Opt("chiếc ô")]
        },
        T.RelationshipMap => new()
        {
            Type = type, TargetAge = 9, AnchorBlockId = "P2", MatchFeedback = "Con nhìn ra rồi!", RelationsJson = Relations(),
            Options =
            [
                Opt("Mai", role: ListeningOptionRole.Node) with { ImageDescription = "Mai trong vườn" },
                Opt("Nam", role: ListeningOptionRole.Node) with { ImageDescription = "Nam trong vườn" },
                Opt("An", role: ListeningOptionRole.Node) with { ImageDescription = "An trong vườn" },
                Opt("giúp đỡ", role: ListeningOptionRole.Card), Opt("bảo vệ", role: ListeningOptionRole.Card),
                Opt("tin tưởng", role: ListeningOptionRole.Card), Opt("cản trở", role: ListeningOptionRole.Card)
            ]
        },
        T.StoryEditor => new()
        {
            Type = type, TargetAge = 9, AnchorBlockId = "P3", MatchFeedback = "Con giữ đúng ý chính!",
            SourceQuote = "Buổi sáng, ba bạn ra vườn. Mai tưới luống hoa. Nam nhặt lá khô. An đặt ô dưới gốc cây. Một con bướm bay qua. Cả ba cùng chăm sóc khu vườn.",
            Options =
            [
                Opt("Buổi sáng, ba bạn ra vườn.") with { CorrectOrder = 1 },
                Opt("Mai tưới luống hoa.", true) with { CorrectOrder = 2, Feedback = "Mai chăm sóc luống hoa." },
                Opt("Nam nhặt lá khô.", true) with { CorrectOrder = 3, Feedback = "Nam cũng chăm sóc vườn." },
                Opt("An đặt ô dưới gốc cây.") with { CorrectOrder = 4 },
                Opt("Một con bướm bay qua.") with { CorrectOrder = 5 },
                Opt("Cả ba cùng chăm sóc khu vườn.", true) with { CorrectOrder = 6, Feedback = "Câu này tóm lại việc ba bạn làm." }
            ]
        },
        T.CharacterTrial => new()
        {
            Type = type, TargetAge = 9, AnchorBlockId = "P4", MatchFeedback = "Cảm ơn con đã suy nghĩ.",
            Prompt = "Trời đổ mưa, Mai muốn bảo vệ luống hoa. Mai quyết định ở lại che luống hoa.",
            SourceQuote = "Mai quyết định ở lại che luống hoa.",
            Options =
            [
                Opt("Mai muốn bảo vệ hoa") with { Feedback = "Mai quan tâm tới hoa.", Counterpoint = "Mai có thể bị ướt." },
                Opt("Mai muốn giúp bạn") with { Feedback = "Mai giữ vườn cùng bạn.", Counterpoint = "Bạn có thể cần Mai nơi khác." },
                Opt("Mai muốn giữ lời hứa") with { Feedback = "Mai nhớ việc mình nhận.", Counterpoint = "Mai có thể nhờ bạn thay." }
            ]
        },
        T.WhatIf => new()
        {
            Type = type, TargetAge = 9, AnchorBlockId = "P5", MatchFeedback = "Cảm ơn con.",
            Prompt = "Nếu Mai không quay lại lấy hạt giống thì điều gì có thể xảy ra?",
            SourceQuote = "Mai quyết định quay lại lấy giúp bạn.",
            Options =
            [
                Opt("Nam sẽ tự tìm hạt giống") with { Feedback = "Nam có thể quay lại tìm." },
                Opt("Hạt giống có thể bị ướt") with { Feedback = "Mưa làm ướt hạt giống." },
                Opt("An sẽ đi cùng Nam") with { Feedback = "An ở vườn nên giúp được." }
            ]
        },
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    private static string[] Codes(ListeningInteractionDraft item)
    {
        var mapped = ListeningInteractionMapper.Resolve([item], Content, Blocks);
        return mapped.Errors.Concat(ListeningInteractionRules.Validate(mapped.Items, Ctx(item.Type))).Select(e => e.Code).Distinct().ToArray();
    }

    private static ListeningInteractionDraft WithOption(ListeningInteractionDraft item, int index, ListeningOptionDraft option) =>
        item with { Options = item.Options.Select((value, i) => i == index ? option : value).ToArray() };

    [Theory]
    [InlineData(T.FindWrongDetail)]
    [InlineData(T.RelationshipMap)]
    [InlineData(T.StoryEditor)]
    [InlineData(T.CharacterTrial)]
    [InlineData(T.WhatIf)]
    public void ValidDrafts_PassTheRules(T type) => Assert.Empty(Codes(Base(type)));

    // ---- FindWrongDetail ----
    [Fact]
    public void FindWrongDetail_SwapThatDoesNotRebuildTheOriginal_IsRejected()
    {
        var item = Base(T.FindWrongDetail) with { Prompt = "Mai, Nam và An cùng chăm sóc khu vườn. Mai đặt chiếc ô gần luống hoa. Nam mang hạt giống tới." };
        Assert.Contains("DETAIL_SWAP_INVALID", Codes(item));
    }

    [Fact]
    public void FindWrongDetail_WrongDetailAppearingTwice_IsRejected()
    {
        var item = Base(T.FindWrongDetail) with
        {
            Prompt = "Mai, Nam và An cùng chăm sóc khu vườn. Mai đặt chiếc ô cạnh luống hoa. Nam mang chiếc ô tới."
        };
        Assert.Contains("DETAIL_SWAP_INVALID", Codes(item));
    }

    [Fact]
    public void FindWrongDetail_NeedsExactlyOneCorrectChoice()
    {
        var item = WithOption(Base(T.FindWrongDetail), 1, Opt("cái xô", true));
        Assert.Contains("MATCH_RULE_INVALID", Codes(item));
    }

    [Fact]
    public void FindWrongDetail_QuoteOfOneSentence_IsRejected()
    {
        var item = Base(T.FindWrongDetail) with { SourceQuote = "Mai đặt bình nước cạnh luống hoa.", Prompt = "Mai đặt chiếc ô cạnh luống hoa." };
        Assert.Contains("LENGTH_LIMIT", Codes(item));
    }

    // ---- RelationshipMap ----
    [Fact]
    public void RelationshipMap_EvidenceMissingACharacterName_IsRejected()
    {
        var item = Base(T.RelationshipMap) with { RelationsJson = Relations(firstEvidence: "Nam gieo hạt.") };
        Assert.Contains("RELATION_EVIDENCE_INVALID", Codes(item));
    }

    [Fact]
    public void RelationshipMap_EvidenceNotInTheStory_IsRejected()
    {
        var item = Base(T.RelationshipMap) with { RelationsJson = Relations(firstEvidence: "Nam giúp Mai tưới cây.") };
        Assert.Contains("RELATION_EVIDENCE_INVALID", Codes(item));
    }

    [Fact]
    public void RelationshipMap_MisunderstandingCardWithoutWordInEvidence_IsRejected()
    {
        var item = Base(T.RelationshipMap) with
        {
            Options = Base(T.RelationshipMap).Options.Select(o => o.Text == "tin tưởng" ? o with { Text = "hiểu lầm" } : o).ToArray()
        };
        Assert.Contains("RELATION_EVIDENCE_INVALID", Codes(item));
    }

    [Fact]
    public void RelationshipMap_CardOutsideTheFiveLabels_IsRejected()
    {
        var item = WithOption(Base(T.RelationshipMap), 6, Opt("ghét bỏ", role: ListeningOptionRole.Card));
        Assert.Contains("RELATION_MAP_INVALID", Codes(item));
    }

    [Fact]
    public void RelationshipMap_CharacterNotYetMet_IsRejected()
    {
        var item = WithOption(Base(T.RelationshipMap), 2, Opt("Bác Gấu", role: ListeningOptionRole.Node) with { ImageDescription = "Bác Gấu" });
        Assert.Contains("RELATION_MAP_INVALID", Codes(item));
    }

    [Fact]
    public void RelationshipMap_CharacterWithoutPicture_IsRejected()
    {
        var item = WithOption(Base(T.RelationshipMap), 0, Opt("Mai", role: ListeningOptionRole.Node));
        Assert.Contains("RELATION_MAP_INVALID", Codes(item));
    }

    [Fact]
    public void RelationshipMap_BrokenJson_IsRejected()
    {
        Assert.Contains("RELATION_MAP_INVALID", Codes(Base(T.RelationshipMap) with { RelationsJson = "{oops" }));
        Assert.Contains("RELATION_MAP_INVALID", Codes(Base(T.RelationshipMap) with { RelationsJson = null }));
    }

    // ---- StoryEditor ----
    [Fact]
    public void StoryEditor_SentencesNotMatchingThePassage_AreRejected()
    {
        var item = WithOption(Base(T.StoryEditor), 4, Opt("Một con chim bay qua.") with { CorrectOrder = 5 });
        Assert.Contains("EDITOR_SENTENCES_INVALID", Codes(item));
    }

    [Fact]
    public void StoryEditor_OneMainIdea_IsRejected()
    {
        var item = Base(T.StoryEditor);
        item = WithOption(item, 2, item.Options[2] with { IsMatch = false, Feedback = null });
        item = WithOption(item, 5, item.Options[5] with { IsMatch = false, Feedback = null });
        Assert.Contains("MAIN_IDEA_COUNT_INVALID", Codes(item));
    }

    [Fact]
    public void StoryEditor_MainSentenceWithoutReason_IsRejected()
    {
        var item = Base(T.StoryEditor);
        item = WithOption(item, 1, item.Options[1] with { Feedback = null });
        Assert.Contains("MAIN_IDEA_COUNT_INVALID", Codes(item));
    }

    [Fact]
    public void StoryEditor_PassageOfFourSentences_IsRejected()
    {
        var item = Base(T.StoryEditor) with
        {
            SourceQuote = "Buổi sáng, ba bạn ra vườn. Mai tưới luống hoa. Nam nhặt lá khô. An đặt ô dưới gốc cây.",
            Options = Base(T.StoryEditor).Options.Take(4).ToArray()
        };
        Assert.Contains("EDITOR_SENTENCES_INVALID", Codes(item));
    }

    // ---- CharacterTrial ----
    [Fact]
    public void CharacterTrial_ReasonWithoutBothSides_IsRejected()
    {
        var item = WithOption(Base(T.CharacterTrial), 0, Opt("Mai muốn bảo vệ hoa") with { Feedback = "Mai quan tâm tới hoa." });
        Assert.Contains("TWO_SIDES_MISSING", Codes(item));
    }

    [Fact]
    public void CharacterTrial_AReasonMarkedCorrect_IsRejected()
    {
        var item = WithOption(Base(T.CharacterTrial), 0,
            Opt("Mai muốn bảo vệ hoa", true) with { Feedback = "Mai quan tâm tới hoa.", Counterpoint = "Mai có thể bị ướt." });
        Assert.Contains("MATCH_RULE_INVALID", Codes(item));
    }

    [Fact]
    public void CharacterTrial_DecisionQuoteNotInTheStory_IsRejected()
    {
        var item = Base(T.CharacterTrial) with { SourceQuote = "Mai bỏ về nhà." };
        Assert.Contains("NOT_GROUNDED", Codes(item));
    }

    [Fact]
    public void CharacterTrial_TwoReasons_IsRejected() =>
        Assert.Contains("OPTION_COUNT_INVALID", Codes(Base(T.CharacterTrial) with { Options = Base(T.CharacterTrial).Options.Take(2).ToArray() }));

    // ---- WhatIf ----
    [Theory]
    [InlineData("Điều gì xảy ra nếu Mai quay lại?")]
    [InlineData("Nếu Mai không quay lại lấy hạt giống")]
    [InlineData("Mai quay lại thì sao?")]
    public void WhatIf_QuestionNotInIfThenForm_IsRejected(string question) =>
        Assert.Contains("SCHEMA_INVALID", Codes(Base(T.WhatIf) with { Prompt = question }));

    [Fact]
    public void WhatIf_OutcomeWithoutBecause_IsRejected()
    {
        var item = WithOption(Base(T.WhatIf), 0, Opt("Nam sẽ tự tìm hạt giống"));
        Assert.Contains("FEEDBACK_MISSING", Codes(item));
    }

    [Fact]
    public void WhatIf_AnOutcomeMarkedCorrect_IsRejected()
    {
        var item = WithOption(Base(T.WhatIf), 0, Opt("Nam sẽ tự tìm hạt giống", true) with { Feedback = "Nam có thể quay lại tìm." });
        Assert.Contains("MATCH_RULE_INVALID", Codes(item));
    }

    // ---- Trường chỉ của loại khác ----
    [Fact]
    public void CounterpointOnAnotherType_IsRejected()
    {
        var item = WithOption(Base(T.WhatIf), 0, Opt("Nam sẽ tự tìm hạt giống") with { Feedback = "Nam tìm.", Counterpoint = "x" });
        Assert.Contains("SCHEMA_INVALID", Codes(item));
    }

    [Fact]
    public void RelationsOnAnotherType_IsRejected() =>
        Assert.Contains("SCHEMA_INVALID", Codes(Base(T.WhatIf) with { RelationsJson = Relations() }));
}
