using StoryPlatform.Application.Features.ListeningInteractions.Models;
using StoryPlatform.Application.Features.ListeningInteractions.Services;
using StoryPlatform.Application.Features.MediaGeneration.Models;
using StoryPlatform.Application.Features.MediaGeneration.Services;
using StoryPlatform.Contracts.AI.Models;
using StoryPlatform.Contracts.AI.Responses;
using StoryPlatform.Domain.Enums;
using Xunit;
using T = StoryPlatform.Domain.Enums.ListeningInteractionType;

namespace StoryPlatform.UnitTests.Features.ListeningInteractions;

public sealed class ListeningInteractionRulesAge8Tests
{
    internal const string Content =
        "Thỏ con và Sóc nhỏ gặp nhau bên bờ suối. Trời mưa to nên Thỏ trú dưới gốc cây.\n\n" +
        "Thỏ hỏi: \"Cậu đang làm gì đấy?\"\nSóc đáp: \"Mình đang tìm hạt dẻ.\"\nThỏ nói: \"Mình giúp cậu nhé!\"\n\n" +
        "Thỏ run lẩy bẩy. Thỏ nghe tiếng sói hú xa xa. Thỏ nấp sau gốc cây.\n\n" +
        "Sói bỏ đi vì trời mưa to. Thỏ thở phào nhẹ nhõm.\n\n" +
        "Sóc cười vui vẻ. Cả hai cùng nhặt hạt dẻ.\n\n" +
        "Từ đó hai bạn luôn giúp nhau.";

    internal static IReadOnlyList<StoryTextBlock> Blocks { get; } = new StoryBlockParser().Parse(Content);

    private static ListeningRuleContext Ctx(T type) => new()
    {
        Type = type, Count = 1, AgeBand = AgeBand.Age_6_8, TargetAge = 8, Content = Content, Blocks = Blocks,
        DialogueBlockIds = ["P2"], GroundingSource = Content
    };

    private static ListeningOptionDraft Opt(string text, bool match = false, ListeningOptionRole role = ListeningOptionRole.None) =>
        new() { Text = text, IsMatch = match, Role = role };

    internal static ListeningInteractionDraft Base(T type) => type switch
    {
        T.AlternateReading => new()
        {
            Type = type, TargetAge = 8, AnchorBlockId = "P2", MatchFeedback = "Con đọc hay quá!",
            Options =
            [
                Opt("Cậu đang làm gì đấy?") with { Speaker = "Thỏ", CorrectOrder = 1 },
                Opt("Mình đang tìm hạt dẻ.") with { Speaker = "Sóc", CorrectOrder = 2 },
                Opt("Mình giúp cậu nhé!") with { Speaker = "Thỏ", CorrectOrder = 3 }
            ]
        },
        T.InferenceQuestion => new()
        {
            Type = type, TargetAge = 8, AnchorBlockId = "P3", Prompt = "Vì sao Thỏ nấp sau gốc cây?",
            SourceQuote = "Thỏ run lẩy bẩy. Thỏ nghe tiếng sói hú xa xa. Thỏ nấp sau gốc cây.",
            Explanation = "Thỏ run và nghe tiếng sói hú nên Thỏ sợ.", MatchFeedback = "Con suy luận giỏi quá!",
            Options =
            [
                Opt("Vì Thỏ sợ có con vật đáng sợ", true), Opt("Vì Thỏ muốn đi ngủ"), Opt("Vì Thỏ đang chơi trốn tìm"),
                Opt("run lẩy bẩy", role: ListeningOptionRole.Clue), Opt("tiếng sói hú", role: ListeningOptionRole.Clue),
                Opt("Thỏ nấp sau gốc cây", role: ListeningOptionRole.Clue)
            ]
        },
        T.CauseEffect => new()
        {
            Type = type, TargetAge = 8, AnchorBlockId = "P4", MatchFeedback = "Con nối giỏi quá!",
            Options =
            [
                Card("Thỏ trú dưới gốc cây", "Thỏ trú dưới gốc cây", ListeningOptionRole.Item, 1),
                Card("Thỏ nấp sau gốc cây", "Thỏ nấp sau gốc cây", ListeningOptionRole.Item, 2),
                Card("Sói bỏ đi", "Sói bỏ đi vì trời mưa to", ListeningOptionRole.Item, 3),
                Card("Trời mưa to", "Trời mưa to nên Thỏ", ListeningOptionRole.Target, 1),
                Card("Thỏ nghe tiếng sói hú", "Thỏ nghe tiếng sói hú xa xa", ListeningOptionRole.Target, 2),
                Card("Mưa rơi rất to", "vì trời mưa to", ListeningOptionRole.Target, 3)
            ]
        },
        T.SceneTitle => new()
        {
            Type = type, TargetAge = 8, AnchorBlockId = "P5", Explanation = "Tiêu đề này nói về cả cảnh Thỏ và Sóc cùng nhau.",
            MatchFeedback = "Con chọn hay quá!",
            Options =
            [
                Opt("Thỏ và Sóc làm bạn", true) with { Feedback = "Tiêu đề này nói về cả hai bạn." },
                Opt("Sóc cười vui vẻ") with { Feedback = "Tiêu đề này chỉ nói về một chi tiết." },
                Opt("Tiếng sói hú xa xa") with { Feedback = "Tiêu đề này chỉ nói về một chi tiết." }
            ]
        },
        _ => new()
        {
            Type = T.ThoughtBubble, TargetAge = 8, AnchorBlockId = "P3", CharacterName = "Thỏ", Prompt = "Thỏ đang nghĩ gì?",
            SourceQuote = "Thỏ run lẩy bẩy.", MatchFeedback = "Con đoán đúng rồi!",
            Options =
            [
                Opt("Mình sợ quá, phải nấp thôi!", true) with { Emotion = "sợ" },
                Opt("Hôm nay vui quá!") with { Emotion = "vui" },
                Opt("Mình muốn ăn cà rốt.") with { Emotion = "háo hức" }
            ]
        }
    };

    private static ListeningOptionDraft Card(string text, string quote, ListeningOptionRole role, int pair) =>
        new() { Text = text, SourceQuote = quote, Role = role, CorrectOrder = pair };

    private static string[] Codes(ListeningInteractionDraft item)
    {
        var mapped = ListeningInteractionMapper.Resolve([item], Content, Blocks);
        return mapped.Errors.Concat(ListeningInteractionRules.Validate(mapped.Items, Ctx(item.Type))).Select(e => e.Code).Distinct().ToArray();
    }

    private static ListeningInteractionDraft Replace(ListeningInteractionDraft item, int index, ListeningOptionDraft option)
    {
        var options = item.Options.ToArray();
        options[index] = option;
        return item with { Options = options };
    }

    [Theory]
    [InlineData(T.AlternateReading)]
    [InlineData(T.InferenceQuestion)]
    [InlineData(T.CauseEffect)]
    [InlineData(T.SceneTitle)]
    [InlineData(T.ThoughtBubble)]
    public void ValidDraft_HasNoErrors(T type) => Assert.Empty(Codes(Base(type)));

    // ---- Đọc luân phiên ----
    [Fact]
    public void AlternateReading_LineNotInTheStory_Rejected() =>
        Assert.Contains("NOT_GROUNDED", Codes(Replace(Base(T.AlternateReading), 1, Opt("Mình đang tìm quả táo.") with { Speaker = "Sóc", CorrectOrder = 2 })));

    [Fact]
    public void AlternateReading_NarrationInsteadOfSpeech_Rejected() =>
        Assert.Contains("NOT_GROUNDED", Codes(Replace(Base(T.AlternateReading), 1, Opt("Sóc đáp") with { Speaker = "Sóc", CorrectOrder = 2 })));

    [Fact]
    public void AlternateReading_OneOrThreeSpeakers_Rejected()
    {
        var item = Base(T.AlternateReading);
        Assert.Contains("SPEAKER_INVALID", Codes(Replace(item, 1, item.Options[1] with { Speaker = "Thỏ" })));
        Assert.Contains("SPEAKER_INVALID", Codes(Replace(item, 2, item.Options[2] with { Speaker = "Gấu" })));
    }

    [Fact]
    public void AlternateReading_SpeakerNotInStory_Rejected()
    {
        var item = Base(T.AlternateReading);
        Assert.Contains("SPEAKER_INVALID", Codes(item with { Options = [item.Options[0], item.Options[1] with { Speaker = "Gấu" }, item.Options[2]] }));
    }

    [Fact]
    public void AlternateReading_OneOrFiveLines_Rejected()
    {
        var item = Base(T.AlternateReading);
        Assert.Contains("OPTION_COUNT_INVALID", Codes(item with { Options = [item.Options[0]] }));
        Assert.Contains("OPTION_COUNT_INVALID", Codes(item with { Options = [.. item.Options, .. item.Options, item.Options[0]] }));
    }

    [Fact]
    public void AlternateReading_LinesOutOfStoryOrder_Rejected()
    {
        var item = Base(T.AlternateReading);
        Assert.Contains("LINE_ORDER_INVALID", Codes(item with
        {
            Options = [item.Options[1] with { CorrectOrder = 1 }, item.Options[0] with { CorrectOrder = 2 }, item.Options[2]]
        }));
    }

    [Fact]
    public void AlternateReading_NotOnADialogueBlock_Rejected() =>
        Assert.Contains("DIALOGUE_BLOCK_INVALID", Codes(Base(T.AlternateReading) with { AnchorBlockId = "P3" }));

    [Fact]
    public void AlternateReading_LineOverTwelveWords_Rejected()
    {
        var item = Base(T.AlternateReading);
        Assert.Contains("LENGTH_LIMIT", Codes(Replace(item, 0, item.Options[0] with { Text = "Cậu đang làm gì đấy?" + new string(' ', 1) + "một hai ba bốn năm sáu bảy tám chín mười mười_một" })));
    }

    [Fact]
    public void AlternateReading_GradingWordInPraise_Rejected() =>
        Assert.Contains("GRADING_WORD", Codes(Base(T.AlternateReading) with { MatchFeedback = "Con đọc sai rồi" }));

    // ---- Suy luận từ chi tiết ----
    [Fact]
    public void Inference_AnswerStatedInThePassage_NotAnInference() =>
        Assert.Contains("NOT_INFERENCE", Codes(Replace(Base(T.InferenceQuestion), 0, Opt("Thỏ nấp sau gốc cây", true))));

    [Fact]
    public void Inference_ClueNotInThePassage_Rejected() =>
        Assert.Contains("CLUE_INVALID", Codes(Replace(Base(T.InferenceQuestion), 3, Opt("con cáo đỏ", role: ListeningOptionRole.Clue))));

    [Fact]
    public void Inference_OnlyTwoClues_Rejected()
    {
        var item = Base(T.InferenceQuestion);
        Assert.Contains("CLUE_INVALID", Codes(item with { Options = item.Options.Take(5).ToArray() }));
    }

    [Fact]
    public void Inference_OverlappingClues_Rejected() =>
        Assert.Contains("CLUE_INVALID", Codes(Replace(Base(T.InferenceQuestion), 4, Opt("lẩy bẩy", role: ListeningOptionRole.Clue))));

    [Fact]
    public void Inference_QuestionMustEndWithQuestionMark() =>
        Assert.Contains("SCHEMA_INVALID", Codes(Base(T.InferenceQuestion) with { Prompt = "Hãy nói vì sao Thỏ nấp." }));

    [Fact]
    public void Inference_PassageOfOneOrFourSentences_Rejected()
    {
        Assert.Contains("LENGTH_LIMIT", Codes(Base(T.InferenceQuestion) with { SourceQuote = "Thỏ run lẩy bẩy." }));
        Assert.Contains("LENGTH_LIMIT", Codes(Base(T.InferenceQuestion) with
        {
            SourceQuote = "Thỏ con và Sóc nhỏ gặp nhau bên bờ suối. Trời mưa to nên Thỏ trú dưới gốc cây."
                + "\n\nThỏ hỏi: \"Cậu đang làm gì đấy?\"\nSóc đáp: \"Mình đang tìm hạt dẻ.\""
        }));
    }

    [Fact]
    public void Inference_PassageNotInTheStory_Rejected() =>
        Assert.Contains("QUOTE_NOT_FOUND", Codes(Base(T.InferenceQuestion) with { SourceQuote = "Thỏ chạy rất nhanh. Thỏ đi đến nhà bạn." }));

    [Fact]
    public void Inference_TwoCorrectAnswers_Rejected() =>
        Assert.Contains("MATCH_RULE_INVALID", Codes(Replace(Base(T.InferenceQuestion), 1, Opt("Vì Thỏ muốn đi ngủ", true))));

    [Fact]
    public void Inference_ExplanationMissingOrTooLong_Rejected()
    {
        Assert.Contains("SCHEMA_INVALID", Codes(Base(T.InferenceQuestion) with { Explanation = null }));
        Assert.Contains("LENGTH_LIMIT", Codes(Base(T.InferenceQuestion) with { Explanation = "Thỏ run. Thỏ sợ. Thỏ nấp." }));
    }

    // ---- Nối nguyên nhân và kết quả ----
    [Fact]
    public void CauseEffect_DuplicatePairNumber_Rejected()
    {
        var item = Base(T.CauseEffect);
        Assert.Contains("PAIR_INVALID", Codes(Replace(item, 1, item.Options[1] with { CorrectOrder = 1 })));
    }

    [Fact]
    public void CauseEffect_CardQuoteNotInTheStory_Rejected()
    {
        var item = Base(T.CauseEffect);
        Assert.Contains("QUOTE_NOT_FOUND", Codes(Replace(item, 2, item.Options[2] with { SourceQuote = "Sói ăn thịt Thỏ" })));
    }

    [Fact]
    public void CauseEffect_CardOverTenWords_Rejected()
    {
        var item = Base(T.CauseEffect);
        Assert.Contains("LENGTH_LIMIT", Codes(Replace(item, 0, item.Options[0] with { Text = "Thỏ trú dưới gốc cây to cạnh bờ suối nhỏ xinh xắn lắm" })));
    }

    [Fact]
    public void CauseEffect_PairWithTheSameQuoteOnBothCards_Rejected()
    {
        var item = Base(T.CauseEffect);
        Assert.Contains("PAIR_INVALID", Codes(Replace(item, 3, item.Options[3] with { SourceQuote = item.Options[0].SourceQuote })));
    }

    [Fact]
    public void CauseEffect_ACorrectFlag_Rejected()
    {
        var item = Base(T.CauseEffect);
        Assert.Contains("MATCH_RULE_INVALID", Codes(Replace(item, 0, item.Options[0] with { IsMatch = true })));
    }

    [Fact]
    public void CauseEffect_FiveCards_Rejected() =>
        Assert.Contains("PAIR_INVALID", Codes(Base(T.CauseEffect) with { Options = Base(T.CauseEffect).Options.Take(5).ToArray() }));

    // ---- Đặt tên cho một cảnh ----
    [Fact]
    public void SceneTitle_TwoCorrectTitles_Rejected()
    {
        var item = Base(T.SceneTitle);
        Assert.Contains("MATCH_RULE_INVALID", Codes(Replace(item, 1, item.Options[1] with { IsMatch = true })));
    }

    [Fact]
    public void SceneTitle_TitleOverEightWords_Rejected()
    {
        var item = Base(T.SceneTitle);
        Assert.Contains("LENGTH_LIMIT", Codes(Replace(item, 0, item.Options[0] with { Text = "Thỏ và Sóc cùng nhau nhặt hạt dẻ bên bờ suối nhỏ" })));
    }

    [Fact]
    public void SceneTitle_OptionWithoutFeedback_Rejected()
    {
        var item = Base(T.SceneTitle);
        Assert.Contains("FEEDBACK_MISSING", Codes(Replace(item, 1, item.Options[1] with { Feedback = null })));
    }

    [Fact]
    public void SceneTitle_NoExplanation_Rejected() =>
        Assert.Contains("SCHEMA_INVALID", Codes(Base(T.SceneTitle) with { Explanation = null }));

    [Fact]
    public void SceneTitle_TwoTitlesOnly_Rejected()
    {
        var item = Base(T.SceneTitle);
        Assert.Contains("OPTION_COUNT_INVALID", Codes(item with { Options = item.Options.Take(2).ToArray() }));
    }

    // ---- Bong bóng suy nghĩ ----
    [Fact]
    public void ThoughtBubble_EmotionOutsideTheList_Rejected()
    {
        var item = Base(T.ThoughtBubble);
        Assert.Contains("EMOTION_INVALID", Codes(Replace(item, 1, item.Options[1] with { Emotion = "giận dữ" })));
    }

    [Fact]
    public void ThoughtBubble_RepeatedEmotion_Rejected()
    {
        var item = Base(T.ThoughtBubble);
        Assert.Contains("EMOTION_INVALID", Codes(Replace(item, 1, item.Options[1] with { Emotion = "sợ" })));
    }

    [Fact]
    public void ThoughtBubble_CharacterNotInTheStory_Rejected() =>
        Assert.Contains("NOT_GROUNDED", Codes(Base(T.ThoughtBubble) with { CharacterName = "Rồng", Prompt = "Rồng đang nghĩ gì?" }));

    [Fact]
    public void ThoughtBubble_QuestionMustNameTheCharacter() =>
        Assert.Contains("SCHEMA_INVALID", Codes(Base(T.ThoughtBubble) with { Prompt = "Bạn ấy đang nghĩ gì?" }));

    [Fact]
    public void ThoughtBubble_SituationQuoteNotInTheStory_Rejected() =>
        Assert.Contains("QUOTE_NOT_FOUND", Codes(Base(T.ThoughtBubble) with { SourceQuote = "Thỏ nhảy múa vui vẻ." }));

    [Fact]
    public void ThoughtBubble_LongThought_Rejected()
    {
        var item = Base(T.ThoughtBubble);
        Assert.Contains("LENGTH_LIMIT", Codes(Replace(item, 0, item.Options[0] with { Text = "một hai ba bốn năm sáu bảy tám chín mười mười_một mười_hai mười_ba" })));
    }

    // ---- Cột không thuộc loại phải để trống ----
    [Fact]
    public void ForeignFields_AreRejected()
    {
        Assert.Contains("SCHEMA_INVALID", Codes(Base(T.SceneTitle) with { SourceQuote = "Sóc cười vui vẻ." }));
        Assert.Contains("SCHEMA_INVALID", Codes(Base(T.ThoughtBubble) with { NoMatchFeedback = "Nhìn lại nhé" }));
        var title = Base(T.SceneTitle);
        Assert.Contains("SCHEMA_INVALID", Codes(Replace(title, 0, title.Options[0] with { Speaker = "Thỏ" })));
        Assert.Contains("SCHEMA_INVALID", Codes(Replace(title, 0, title.Options[0] with { Emotion = "vui" })));
    }

    [Fact]
    public void Age8Types_AreNeverEndOfStory() =>
        Assert.All(new[] { T.AlternateReading, T.InferenceQuestion, T.CauseEffect, T.SceneTitle, T.ThoughtBubble },
            type => Assert.False(ListeningInteractionRules.IsEndOfStory(type, 8)));

    // ---- Phản hồi của AI → bản nháp → luật ----
    private static GenerateListeningInteractionResponse AiResponse(T type) => type switch
    {
        T.AlternateReading => new() { AlternateReadings = [new AlternateReadingItemDto { AfterBlockId = "P2", PraiseFeedback = "Con đọc hay quá!",
            Lines = [new("Thỏ", "Cậu đang làm gì đấy?"), new("Sóc", "Mình đang tìm hạt dẻ."), new("Thỏ", "Mình giúp cậu nhé!")] }] },
        T.InferenceQuestion => new() { InferenceQuestions = [new InferenceQuestionItemDto { AfterBlockId = "P3", Question = "Vì sao Thỏ nấp sau gốc cây?",
            Passage = "Thỏ run lẩy bẩy. Thỏ nghe tiếng sói hú xa xa. Thỏ nấp sau gốc cây.", Explanation = "Thỏ run và nghe tiếng sói hú nên Thỏ sợ.",
            MatchFeedback = "Con suy luận giỏi quá!", Clues = ["run lẩy bẩy", "tiếng sói hú", "Thỏ nấp sau gốc cây"],
            Answers = [new("Vì Thỏ sợ có con vật đáng sợ", true), new("Vì Thỏ muốn đi ngủ", false), new("Vì Thỏ đang chơi trốn tìm", false)] }] },
        T.CauseEffect => new() { CauseEffects = [new CauseEffectItemDto { AfterBlockId = "P4", MatchFeedback = "Con nối giỏi quá!", Pairs =
            [new(new("Thỏ trú dưới gốc cây", "Thỏ trú dưới gốc cây"), new("Trời mưa to", "Trời mưa to nên Thỏ")),
             new(new("Thỏ nấp sau gốc cây", "Thỏ nấp sau gốc cây"), new("Thỏ nghe tiếng sói hú", "Thỏ nghe tiếng sói hú xa xa")),
             new(new("Sói bỏ đi", "Sói bỏ đi vì trời mưa to"), new("Mưa rơi rất to", "vì trời mưa to"))] }] },
        T.SceneTitle => new() { SceneTitles = [new SceneTitleItemDto { AfterBlockId = "P5", Explanation = "Tiêu đề này nói về cả cảnh Thỏ và Sóc cùng nhau.",
            MatchFeedback = "Con chọn hay quá!", Titles = [new("Thỏ và Sóc làm bạn", true, "Tiêu đề này nói về cả hai bạn."),
                new("Sóc cười vui vẻ", false, "Tiêu đề này chỉ nói về một chi tiết."), new("Tiếng sói hú xa xa", false, "Tiêu đề này chỉ nói về một chi tiết.")] }] },
        _ => new() { ThoughtBubbles = [new ThoughtBubbleItemDto { AfterBlockId = "P3", CharacterName = "Thỏ", Question = "Thỏ đang nghĩ gì?",
            SourceQuote = "Thỏ run lẩy bẩy.", MatchFeedback = "Con đoán đúng rồi!", Bubbles =
            [new("Mình sợ quá, phải nấp thôi!", true, "sợ"), new("Hôm nay vui quá!", false, "vui"), new("Mình muốn ăn cà rốt.", false, "háo hức")] }] }
    };

    [Theory]
    [InlineData(T.AlternateReading)]
    [InlineData(T.InferenceQuestion)]
    [InlineData(T.CauseEffect)]
    [InlineData(T.SceneTitle)]
    [InlineData(T.ThoughtBubble)]
    public void AiResponse_MapsToDraftsThatPassTheRules(T type)
    {
        var drafts = ListeningInteractionAiMapper.ToDrafts(AiResponse(type), type).Select(d => d with { TargetAge = 8 }).ToArray();
        var draft = Assert.Single(drafts);
        var mapped = ListeningInteractionMapper.Resolve(drafts, Content, Blocks);
        Assert.Empty(mapped.Errors);
        Assert.Empty(ListeningInteractionRules.Validate(mapped.Items, Ctx(type)));
        // Cùng nội dung với bản nháp dựng tay của các test luật.
        Assert.Equal(Base(type).Options.Select(o => (o.Text, o.Role, o.CorrectOrder, o.Speaker, o.Emotion, o.IsMatch)),
            draft.Options.Select(o => (o.Text, o.Role, o.CorrectOrder, o.Speaker, o.Emotion, o.IsMatch)));
    }

    [Fact]
    public void Mapper_ResolvesCardQuotesAndRoundTripsSpeakerAndEmotionThroughEntities()
    {
        var cause = ListeningInteractionMapper.Resolve([Base(T.CauseEffect)], Content, Blocks).Items.Single();
        Assert.All(cause.Options, o => Assert.NotNull(o.SourceStart));
        var thought = ListeningInteractionMapper.Resolve([Base(T.ThoughtBubble)], Content, Blocks).Items.Single();
        var (interaction, options) = ListeningInteractionMapper.ToEntities(thought, 1);
        Assert.Equal(["sợ", "vui", "háo hức"], options.Select(o => o.Emotion));
        var back = ListeningInteractionMapper.ToDraft(interaction, options, Blocks);
        Assert.Equal(["sợ", "vui", "háo hức"], back.Options.Select(o => o.Emotion));
        var lines = ListeningInteractionMapper.Resolve([Base(T.AlternateReading)], Content, Blocks).Items.Single();
        var (_, lineOptions) = ListeningInteractionMapper.ToEntities(lines, 1);
        Assert.Equal(["Thỏ", "Sóc", "Thỏ"], lineOptions.Select(o => o.Speaker));
    }

    // Bộ tuổi 8 dùng chung cho các test luồng (ListeningTestStory) phải hợp lệ trong đúng vùng của kế hoạch.
    [Theory]
    [InlineData(T.AlternateReading)]
    [InlineData(T.InferenceQuestion)]
    [InlineData(T.CauseEffect)]
    [InlineData(T.SceneTitle)]
    [InlineData(T.ThoughtBubble)]
    public void SharedTestStoryAge8Responses_PassTheRules(T type)
    {
        var story = ListeningTestStory.Content;
        var blocks = ListeningTestStory.Blocks;
        var plan = ListeningInteractionPlanner.Create(8, blocks);
        var item = plan.Items.Single(x => x.Type == type);
        var drafts = ListeningInteractionAiMapper.ToDrafts(ListeningTestStory.Age8AiResponse(type), type).Select(d => d with { TargetAge = 8 }).ToArray();
        var mapped = ListeningInteractionMapper.Resolve(drafts, story, blocks);
        var context = new ListeningRuleContext
        {
            Type = type, Count = 1, AgeBand = AgeBand.Age_6_8, TargetAge = 8, Content = story, Blocks = blocks,
            DialogueBlockIds = plan.DialogueBlockIds, GroundingSource = story, Zones = item.Zones
        };
        var errors = mapped.Errors.Concat(ListeningInteractionRules.Validate(mapped.Items, context)).Select(e => $"{e.Code}@{e.Field}").ToArray();
        Assert.Empty(errors);
    }
}
