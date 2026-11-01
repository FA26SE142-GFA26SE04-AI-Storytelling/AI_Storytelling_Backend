using StoryPlatform.Application.Features.ListeningInteractions.Models;
using StoryPlatform.Application.Features.ListeningInteractions.Services;
using StoryPlatform.Domain.Enums;
using Xunit;
using S = StoryPlatform.UnitTests.Features.ListeningInteractions.ListeningTestStory;
using T = StoryPlatform.Domain.Enums.ListeningInteractionType;

namespace StoryPlatform.UnitTests.Features.ListeningInteractions;

public sealed class ListeningInteractionRulesTests
{
    private static IReadOnlyList<ListeningInteractionDraft> Resolve(params ListeningInteractionDraft[] items) =>
        ListeningInteractionMapper.Resolve(items, S.Content, S.Blocks).Items;

    private static string[] Codes(IReadOnlyList<ListeningInteractionDraft> items, ListeningRuleContext context) =>
        ListeningInteractionRules.Validate(items, context).Select(e => e.Code).Distinct().ToArray();

    private static ListeningInteractionDraft[] TwoCheckpoints() =>
    [
        S.Checkpoint("P1", CheckpointKind.Emotion, "Trời bắt đầu tối."),
        S.Checkpoint("P2", CheckpointKind.Prediction, "Thỏ nghe tiếng sột soạt sau bụi cây.")
    ];

    [Fact]
    public void ValidCheckpoints_Pass() =>
        Assert.Empty(ListeningInteractionRules.Validate(Resolve(TwoCheckpoints()), S.Context(T.Checkpoint, 2)));

    [Fact]
    public void WrongCount_IsRejected() =>
        Assert.Contains("COUNT_MISMATCH", Codes(Resolve(TwoCheckpoints()), S.Context(T.Checkpoint, 3)));

    [Fact]
    public void AnchorOnLastBlockOrOccupiedOrDuplicate_IsRejected()
    {
        var last = S.Checkpoint("P6", CheckpointKind.Emotion, "Trời bắt đầu tối.");
        Assert.Contains("ANCHOR_INVALID", Codes(Resolve(last), S.Context(T.Checkpoint, 1)));

        var occupied = S.Context(T.Checkpoint, 2) with { OccupiedAnchorOffsets = [S.Block("P1").EndOffset] };
        Assert.Contains("ANCHOR_INVALID", Codes(Resolve(TwoCheckpoints()), occupied));

        var twice = TwoCheckpoints();
        twice[1] = twice[1] with { AnchorBlockId = "P1", SourceQuote = "Trời bắt đầu tối." };
        Assert.Contains("ANCHOR_INVALID", Codes(Resolve(twice), S.Context(T.Checkpoint, 2)));
    }

    [Fact]
    public void Checkpoint_KindLimitAndEmotionRequirement()
    {
        var predictions = new[]
        {
            S.Checkpoint("P1", CheckpointKind.Prediction, "Trời bắt đầu tối."),
            S.Checkpoint("P2", CheckpointKind.Prediction, "Thỏ nghe tiếng sột soạt sau bụi cây.")
        };
        Assert.Contains("KIND_LIMIT", Codes(Resolve(predictions), S.Context(T.Checkpoint, 2)));
    }

    [Fact]
    public void Checkpoint_OptionCountAndMatchRules()
    {
        var items = TwoCheckpoints();
        items[0] = items[0] with { Options = items[0].Options.Take(3).ToArray() }; // Emotion cần 4
        Assert.Contains("OPTION_COUNT_INVALID", Codes(Resolve(items), S.Context(T.Checkpoint, 2)));

        items = TwoCheckpoints();
        items[1] = items[1] with { Options = items[1].Options.Select(o => o with { IsMatch = true }).ToArray() };
        Assert.Contains("MATCH_RULE_INVALID", Codes(Resolve(items), S.Context(T.Checkpoint, 2)));

        var opinion = S.Checkpoint("P3", CheckpointKind.Opinion, "Trời bắt đầu tối.");
        opinion = opinion with { Options = opinion.Options.Select((o, i) => o with { IsMatch = i == 0 }).ToArray() };
        Assert.Contains("MATCH_RULE_INVALID", Codes(Resolve(TwoCheckpoints()[0], opinion), S.Context(T.Checkpoint, 2)));
    }

    [Fact]
    public void Checkpoint_MissingFeedback_IsRejected()
    {
        var items = TwoCheckpoints();
        items[0] = items[0] with { NoMatchFeedback = " " };
        Assert.Contains("FEEDBACK_MISSING", Codes(Resolve(items), S.Context(T.Checkpoint, 2)));
    }

    [Fact]
    public void FieldOutsideType_IsSchemaInvalid()
    {
        var items = TwoCheckpoints();
        items[0] = items[0] with { HiddenWord = "Gấu" };
        Assert.Contains(new ListeningValidationError("SCHEMA_INVALID", 0, "hiddenWord"),
            ListeningInteractionRules.Validate(Resolve(items), S.Context(T.Checkpoint, 2)));
    }

    [Fact]
    public void GradingWordAndUnsafeContent_AreRejected()
    {
        var items = TwoCheckpoints();
        items[1] = items[1] with { Options = [items[1].Options[0], items[1].Options[1] with { Feedback = "Con trả lời Sai rồi." }] };
        Assert.Contains("GRADING_WORD", Codes(Resolve(items), S.Context(T.Checkpoint, 2)));

        var unsafeContext = S.Context(T.Checkpoint, 2) with { IsSafe = text => !text.Contains("cơn gió") };
        Assert.Contains("UNSAFE_CONTENT", Codes(Resolve(TwoCheckpoints()), unsafeContext));
    }

    [Fact]
    public void GradingWord_MatchesWholeSyllables()
    {
        Assert.True(ListeningInteractionRules.ContainsGradingWord("Chưa đúng lắm.", ListeningInteractionRules.DefaultGradingWords));
        Assert.False(ListeningInteractionRules.ContainsGradingWord("Con nói đúng rồi!", ListeningInteractionRules.DefaultGradingWords));
        Assert.False(ListeningInteractionRules.ContainsGradingWord("Sải cánh bay.", ListeningInteractionRules.DefaultGradingWords));
    }

    private static ListeningInteractionDraft RolePlay(string anchor = "P2", string quote = "Có ai ở đó không?") => new()
    {
        Type = T.RolePlay, AnchorBlockId = anchor, CharacterName = "Thỏ", SourceQuote = quote,
        MatchFeedback = "Con đã thử nói giống Thỏ rồi, giỏi lắm!"
    };

    [Fact]
    public void RolePlay_ValidAndDialogueRules()
    {
        Assert.Empty(ListeningInteractionRules.Validate(Resolve(RolePlay()), S.Context(T.RolePlay, 1)));
        var noDialogue = S.Context(T.RolePlay, 1) with { DialogueBlockIds = [] };
        Assert.Contains("DIALOGUE_BLOCK_INVALID", Codes(Resolve(RolePlay()), noDialogue));
    }

    [Fact]
    public void LengthLimits_AreEnforced()
    {
        Assert.Equal(12, ListeningInteractionRules.RolePlayMaxWords);
        Assert.Equal(3, ListeningInteractionRules.CountWords("Có ai, ở!"));
        var longLine = RolePlay("P2", "Thỏ nghe tiếng sột soạt sau bụi cây. Thỏ hỏi: \"Có ai ở đó không?\"");
        Assert.Contains("LENGTH_LIMIT", Codes(Resolve(longLine), S.Context(T.RolePlay, 1)));
    }

    private static ListeningInteractionDraft FillBlank() => new()
    {
        Type = T.FillBlank, AnchorBlockId = "P3", SourceQuote = "Hóa ra đó là bác Gấu đến giúp Thỏ tìm đường về.",
        HiddenWord = "Gấu", Hint = "Đó là một con vật to, lông nâu.",
        MatchFeedback = "Đúng rồi, đó là bác Gấu!", NoMatchFeedback = "Từ trong truyện là \"Gấu\" đó. Cùng nghe tiếp nhé!",
        Options = [new() { Text = "Gấu" }, new() { Text = "bác Gấu" }]
    };

    [Fact]
    public void FillBlank_ValidAndBlankRules()
    {
        var context = S.Context(T.FillBlank, 1, AgeBand.Age_9_10);
        Assert.Empty(ListeningInteractionRules.Validate(Resolve(FillBlank()), context));
        Assert.Contains("BLANK_INVALID", Codes(Resolve(FillBlank() with { HiddenWord = "Sói" }), context));
        Assert.Contains("BLANK_INVALID", Codes(Resolve(FillBlank() with { Hint = "Con gấu to" }), context));
        Assert.Contains("BLANK_INVALID", Codes(Resolve(FillBlank() with { Options = [new() { Text = "bác Gấu" }] }), context));
    }

    private static ListeningInteractionDraft Ordering() => new()
    {
        Type = T.EventOrdering, AnchorBlockId = "P4",
        MatchFeedback = "Con nhớ giỏi quá, đúng thứ tự rồi!", NoMatchFeedback = "Mình cùng nghe lại đoạn này rồi xếp lại nhé.",
        Options =
        [
            new() { Text = "Thỏ đi lạc vào rừng", CorrectOrder = 1, BlockId = "P1", SourceQuote = "Thỏ con đi lạc vào rừng." },
            new() { Text = "Thỏ nghe tiếng sột soạt", CorrectOrder = 2, BlockId = "P2", SourceQuote = "Thỏ nghe tiếng sột soạt sau bụi cây." },
            new() { Text = "Bác Gấu dẫn Thỏ về nhà", CorrectOrder = 3, BlockId = "P4", SourceQuote = "Bác Gấu dẫn Thỏ về tới cửa hang." }
        ]
    };

    [Fact]
    public void EventOrdering_ValidAndOrderRules()
    {
        var context = S.Context(T.EventOrdering, 1);
        Assert.Empty(ListeningInteractionRules.Validate(Resolve(Ordering()), context));
        Assert.Contains("EVENT_ORDER_INVALID", Codes(Resolve(Ordering() with { AnchorBlockId = "P5" }), context));
        var swapped = Ordering();
        swapped = swapped with { Options = [swapped.Options[1] with { CorrectOrder = 1 }, swapped.Options[0] with { CorrectOrder = 2 }, swapped.Options[2]] };
        Assert.Contains("EVENT_ORDER_INVALID", Codes(Resolve(swapped), context));
        // MatchFeedback được nói "đúng rồi".
        Assert.DoesNotContain("GRADING_WORD", Codes(Resolve(Ordering()), context));
    }

    private static ListeningInteractionDraft Picture() => new()
    {
        Type = T.PicturePrediction, AnchorBlockId = "P2", RevealBlockId = "P3",
        Prompt = "Ai đang gây ra tiếng sột soạt sau bụi cây?", SourceQuote = "Thỏ nghe tiếng sột soạt sau bụi cây.",
        WaitFeedback = "Con chọn rồi! Mình cùng nghe tiếp xem nhé.",
        Options =
        [
            new() { Text = "Một bác gấu hiền lành", ImageDescription = "Gấu nâu to, mặt hiền, đứng cạnh bụi cây", Feedback = "Đúng là bác Gấu đó!", IsMatch = true },
            new() { Text = "Một bạn sóc nhỏ", ImageDescription = "Sóc nhỏ đuôi xù trên cành cây thấp", Feedback = "Ý hay đó!" }
        ]
    };

    [Fact]
    public void PicturePrediction_ValidAndRevealRules()
    {
        var context = S.Context(T.PicturePrediction, 1);
        Assert.Empty(ListeningInteractionRules.Validate(Resolve(Picture()), context));
        Assert.Contains("REVEAL_BLOCK_INVALID", Codes(Resolve(Picture() with { RevealBlockId = "P1" }), context));
        Assert.Contains("MATCH_RULE_INVALID",
            Codes(Resolve(Picture() with { Options = Picture().Options.Select(o => o with { IsMatch = false }).ToArray() }), context));
    }

    [Fact]
    public void CheckStale_DetectsShiftedContent()
    {
        var item = Resolve(TwoCheckpoints())[0];
        Assert.Empty(ListeningInteractionRules.CheckStale(item, 0, S.Content, S.Blocks));
        var edited = "Mở đầu mới. " + S.Content;
        var editedBlocks = new StoryPlatform.Application.Features.MediaGeneration.Services.StoryBlockParser().Parse(edited);
        Assert.Equal([new ListeningValidationError("STALE_ANCHOR", 0, null)],
            ListeningInteractionRules.CheckStale(item, 0, edited, editedBlocks));
    }

    [Fact]
    public void UngroundedQuestion_IsRejected_ButOptionTextIsNotChecked()
    {
        var items = TwoCheckpoints();
        items[0] = items[0] with { Prompt = "Hành tinh nào xa nhất?" };
        Assert.Contains(new ListeningValidationError("NOT_GROUNDED", 0, "question"),
            ListeningInteractionRules.Validate(Resolve(items), S.Context(T.Checkpoint, 2)));
        // Thẻ cảm xúc không có trong truyện vẫn hợp lệ.
        Assert.DoesNotContain(ListeningInteractionRules.Validate(Resolve(TwoCheckpoints()), S.Context(T.Checkpoint, 2)),
            e => e.Code == "NOT_GROUNDED");
    }
}
