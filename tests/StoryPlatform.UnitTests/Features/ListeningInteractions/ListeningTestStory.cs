using StoryPlatform.Application.Features.ListeningInteractions.Models;
using StoryPlatform.Application.Features.MediaGeneration.Models;
using StoryPlatform.Application.Features.MediaGeneration.Services;
using StoryPlatform.Domain.Enums;
using StoryPlatform.Contracts.AI.Responses;

namespace StoryPlatform.UnitTests.Features.ListeningInteractions;

/// <summary>Truyện 6 đoạn (P1..P6) dùng chung; P2 có câu thoại.</summary>
internal static class ListeningTestStory
{
    public const string Content =
        "Thỏ con đi lạc vào rừng. Trời bắt đầu tối.\n\n" +
        "Thỏ nghe tiếng sột soạt sau bụi cây. Thỏ hỏi: \"Có ai ở đó không?\"\n\n" +
        "Hóa ra đó là bác Gấu đến giúp Thỏ tìm đường về.\n\n" +
        "Bác Gấu dẫn Thỏ về tới cửa hang.\n\n" +
        "Mẹ Thỏ ôm Thỏ vào lòng.\n\n" +
        "Từ đó Thỏ luôn nhờ người lớn khi bị lạc.";

    public static IReadOnlyList<StoryTextBlock> Blocks { get; } = new StoryBlockParser().Parse(Content);

    public static StoryTextBlock Block(string id) => Blocks.Single(b => b.BlockId == id);

    public static ListeningRuleContext Context(ListeningInteractionType type, int count, AgeBand age = AgeBand.Age_6_8) => new()
    {
        Type = type,
        Count = count,
        AgeBand = age,
        Content = Content,
        Blocks = Blocks,
        DialogueBlockIds = ["P2"],
        GroundingSource = Content
    };

    public static ListeningInteractionDraft Checkpoint(string anchor, CheckpointKind kind, string quote) => new()
    {
        Type = ListeningInteractionType.Checkpoint,
        AnchorBlockId = anchor,
        Kind = kind,
        // Câu hỏi phải có từ ≥ 4 ký tự xuất hiện trong truyện (NOT_GROUNDED).
        Prompt = kind == CheckpointKind.Prediction ? "Tiếng sột soạt đó là của ai?" : "Trong rừng tối, con nghĩ gì về Thỏ?",
        SourceQuote = quote,
        Explanation = "Đoạn sau cho biết đó là bác Gấu.",
        NoMatchFeedback = "Con nghĩ thú vị quá! Cùng nghe tiếp nhé.",
        Options = kind switch
        {
            CheckpointKind.Emotion =>
            [
                new() { Text = "Thỏ đang sợ", Feedback = "Ừ, trời tối Thỏ thấy sợ.", IsMatch = true },
                new() { Text = "Thỏ đang vui", Feedback = "Cũng có thể lắm!" },
                new() { Text = "Thỏ đang buồn ngủ", Feedback = "Ý hay đó!" },
                new() { Text = "Thỏ đang tò mò", Feedback = "Thỏ cũng tò mò lắm!" }
            ],
            CheckpointKind.Opinion =>
            [
                new() { Text = "Con thích bác Gấu", Feedback = "Bác Gấu tốt bụng nhỉ!" },
                new() { Text = "Con muốn giúp Thỏ", Feedback = "Con thật tốt bụng!" }
            ],
            _ =>
            [
                new() { Text = "Một bạn thú tốt bụng", Feedback = "Ý hay đó! Mình cùng nghe tiếp nhé.", IsMatch = true },
                new() { Text = "Một cơn gió mạnh", Feedback = "Cũng có thể lắm! Nghe tiếp xem sao nhé." }
            ]
        }
    };
    /// <summary>
    /// Response AI hợp lệ cho truyện mẫu, đặt chỗ không đụng nhau theo kế hoạch 6-8:
    /// Checkpoint sau P1 và P3, RolePlay sau P2, PicturePrediction sau P4 (đáp án ở P5), EventOrdering sau đoạn cuối.
    /// </summary>
    public static GenerateListeningInteractionResponse AiResponse(ListeningInteractionType type) => type switch
    {
        ListeningInteractionType.Checkpoint => new()
        {
            Checkpoints =
            [
                new()
                {
                    Kind = "Emotion", AfterBlockId = "P1", Question = "Trong rừng tối, con nghĩ gì về Thỏ?",
                    SourceQuote = "Trời bắt đầu tối.", Explanation = "Trời tối nên Thỏ thấy sợ.",
                    OtherFeedback = "Con nghĩ thú vị quá! Cùng nghe tiếp nhé.",
                    Options =
                    [
                        new() { Text = "Thỏ đang sợ", Feedback = "Ừ, trời tối Thỏ thấy sợ.", IsMatch = true },
                        new() { Text = "Thỏ đang vui", Feedback = "Cũng có thể lắm!" },
                        new() { Text = "Thỏ đang buồn ngủ", Feedback = "Ý hay đó!" },
                        new() { Text = "Thỏ đang tò mò", Feedback = "Thỏ cũng tò mò lắm!" }
                    ]
                },
                new()
                {
                    Kind = "Prediction", AfterBlockId = "P3", Question = "Tiếng sột soạt đó là của ai?",
                    SourceQuote = "Hóa ra đó là bác Gấu đến giúp Thỏ tìm đường về.", Explanation = "Đoạn này cho biết đó là bác Gấu.",
                    OtherFeedback = "Con nghĩ thú vị quá! Cùng nghe tiếp nhé.",
                    Options =
                    [
                        new() { Text = "Bác Gấu tốt bụng", Feedback = "Ý hay đó! Mình cùng nghe tiếp nhé.", IsMatch = true },
                        new() { Text = "Một cơn gió mạnh", Feedback = "Cũng có thể lắm! Nghe tiếp xem sao nhé." }
                    ]
                }
            ]
        },
        ListeningInteractionType.RolePlay => new()
        {
            RolePlays =
            [
                new()
                {
                    AfterBlockId = "P2", CharacterName = "Thỏ", LineQuote = "Có ai ở đó không?",
                    PraiseFeedback = "Con đã thử nói giống Thỏ rồi, giỏi lắm!"
                }
            ]
        },
        ListeningInteractionType.FillBlank => new()
        {
            FillBlanks =
            [
                new()
                {
                    AfterBlockId = "P3", SentenceQuote = "Hóa ra đó là bác Gấu đến giúp Thỏ tìm đường về.", HiddenWord = "Gấu",
                    AcceptedAnswers = ["Gấu", "bác Gấu"], Hint = "Đó là một con vật to, lông nâu.",
                    MatchFeedback = "Đúng rồi, đó là bác Gấu!", SuggestFeedback = "Từ trong truyện là \"Gấu\" đó. Cùng nghe tiếp nhé!"
                }
            ]
        },
        ListeningInteractionType.EventOrdering => new()
        {
            EventOrderings =
            [
                new()
                {
                    Events =
                    [
                        new() { Order = 1, BlockId = "P1", Text = "Thỏ đi lạc vào rừng", SourceQuote = "Thỏ con đi lạc vào rừng." },
                        new() { Order = 2, BlockId = "P3", Text = "Bác Gấu đến giúp Thỏ", SourceQuote = "Hóa ra đó là bác Gấu đến giúp Thỏ tìm đường về." },
                        new() { Order = 3, BlockId = "P6", Text = "Thỏ biết nhờ người lớn giúp", SourceQuote = "Từ đó Thỏ luôn nhờ người lớn khi bị lạc." }
                    ],
                    MatchFeedback = "Con nhớ giỏi quá, đúng thứ tự rồi!",
                    RetryFeedback = "Mình cùng nghe lại mấy đoạn này rồi xếp lại nhé."
                }
            ]
        },
        ListeningInteractionType.PicturePrediction => new()
        {
            PicturePredictions =
            [
                new()
                {
                    AfterBlockId = "P4", RevealBlockId = "P5", Question = "Ai đang chờ Thỏ ở cửa hang?",
                    SourceQuote = "Bác Gấu dẫn Thỏ về tới cửa hang.", WaitFeedback = "Con chọn rồi! Mình cùng nghe tiếp xem nhé.",
                    Options =
                    [
                        new() { Label = "Mẹ Thỏ", ImageDescription = "Thỏ mẹ lông trắng đứng trước cửa hang", Feedback = "Đúng là mẹ Thỏ đang chờ đó!", IsMatch = true },
                        new() { Label = "Một bạn sóc nhỏ", ImageDescription = "Sóc nhỏ đuôi xù trên cành cây thấp", Feedback = "Ý hay đó!" }
                    ]
                }
            ]
        },
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };
}
