using StoryPlatform.Application.Features.ListeningInteractions.Models;
using StoryPlatform.Application.Features.MediaGeneration.Models;
using StoryPlatform.Application.Features.MediaGeneration.Services;
using StoryPlatform.Domain.Enums;

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
}
