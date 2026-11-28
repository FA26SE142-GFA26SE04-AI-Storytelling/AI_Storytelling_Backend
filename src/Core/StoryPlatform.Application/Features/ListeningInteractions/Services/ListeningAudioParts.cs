using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ListeningInteractions.Services;

/// <param name="Required">
/// False: vẫn tạo audio nhưng thiếu thì không ẩn tương tác, không chặn readiness (truyện cũ tạo trước khi có phần này).
/// </param>
public sealed record ListeningAudioPart(int? OptionId, string PartKey, string Text, bool Required = true);

/// <summary>Các câu cần đọc của tương tác; bỏ qua trường rỗng và option đã xoá.</summary>
public static class ListeningAudioParts
{
    public const string Prompt = "prompt", Wait = "wait", Match = "match", NoMatch = "noMatch",
        Hint = "hint", Text = "text", Feedback = "feedback", Instruction = "instruction";
    public const string Name = "name", Sound = "sound", RetryLook = "retryLook", RetryArrow = "retryArrow", Unclear = "unclear";
    public const string RetryLookClose = "retryLookClose", RetryHear = "retryHear", RetryTry = "retryTry", Sentence = "sentence",
        TruthYes = "truthYes", TruthNo = "truthNo", Explanation = "explanation", Chain = "chain";

    /// <summary>
    /// Phần âm thanh cần mốc thời gian từng chữ: câu bé nhìn chữ đi theo giọng đọc ở mức tương tác, và từng lượt thoại
    /// của Đọc luân phiên (mức option).
    /// </summary>
    public static bool NeedsWordTimings(ListeningInteractionType type, int? optionId, string? partKey) =>
        partKey is not null && (optionId is null
            ? HighlightKeys.Contains(partKey) || partKey.StartsWith(Sentence + ":", StringComparison.Ordinal)
            : type == ListeningInteractionType.AlternateReading && partKey == Text);

    public static string SentenceKey(int orderIndex) => orderIndex == 0 ? Sentence : $"sentence:{orderIndex + 1}";

    /// <summary>Phần âm thanh mà bé nhìn chữ đi theo giọng đọc nên cần mốc thời gian từng chữ.</summary>
    public static IReadOnlyList<string> HighlightKeys { get; } = [Text, Name, Prompt, Sentence];

    public static string FixedLine(string partKey) => partKey switch
    {
        RetryLook => "Bé nhìn lại tranh nhé.",
        RetryArrow => "Đi theo mũi tên nhé.",
        Unclear => "Mình nghe chưa rõ, mình đọc lại nhé.",
        RetryLookClose => "Nhìn kỹ lại nhé.",
        RetryHear => "Nghe lạ quá, mình thử lại nhé.",
        RetryTry => "Bé thử lại nhé.",
        TruthYes => "Đúng rồi, truyện kể như vậy đó.",
        TruthNo => "Truyện không kể như vậy đâu, mình cùng xem lại nhé.",
        _ => throw new ArgumentOutOfRangeException(nameof(partKey))
    };

    /// <summary>
    /// Câu hướng dẫn cố định cho loại không có câu hỏi (Prompt), để bé chưa biết chữ nghe được phải làm gì.
    /// Câu do hệ thống viết sẵn, không do AI sinh. Null với loại đã có Prompt hoặc không hiện cho trẻ.
    /// </summary>
    public static string? InstructionFor(ListeningInteraction interaction) => interaction.Type switch
    {
        ListeningInteractionType.CharacterLetter => string.IsNullOrWhiteSpace(interaction.CharacterName)
            ? "Em hãy chọn chữ còn thiếu trong tên nhân vật nhé!"
            : $"Em hãy chọn chữ còn thiếu trong tên bạn {interaction.CharacterName.Trim()} nhé!",
        ListeningInteractionType.WhoSaidIt => "Em hãy nghe rồi chạm vào bạn đã nói câu này nhé!",
        ListeningInteractionType.RolePlay => string.IsNullOrWhiteSpace(interaction.CharacterName)
            ? "Em hãy nghe mẫu rồi nói theo câu này nhé!"
            : $"Em hãy đóng vai {interaction.CharacterName.Trim()}, nghe mẫu rồi nói theo câu này nhé!",
        ListeningInteractionType.FillBlank => "Em hãy nghe câu rồi điền chữ còn thiếu vào chỗ trống nhé!",
        ListeningInteractionType.EventOrdering => "Em hãy chạm vào các thẻ theo đúng thứ tự chuyện xảy ra trong truyện nhé!",
        ListeningInteractionType.PictureSearch => "Em hãy nghe rồi chạm vào đồ vật trong tranh nhé!",
        ListeningInteractionType.TrueFalse => "Em hãy nghe câu rồi chạm dấu đúng hoặc dấu sai nhé!",
        ListeningInteractionType.FollowAction => "Em hãy kéo đồ vật đến đúng chỗ giúp bạn nhé!",
        ListeningInteractionType.SentenceBuilder => "Em hãy kéo các thẻ chữ thành câu rồi đọc to câu đó nhé!",
        ListeningInteractionType.AlternateReading => "Em hãy chọn một bạn rồi cùng đọc lời của bạn ấy nhé!",
        ListeningInteractionType.InferenceQuestion => "Em hãy nghe đoạn truyện rồi chọn câu trả lời phù hợp nhé!",
        ListeningInteractionType.CauseEffect => "Em hãy kéo nối chuyện đã xảy ra với lí do của nó nhé!",
        ListeningInteractionType.SceneTitle => "Em hãy chọn tiêu đề hợp nhất với cảnh vừa nghe nhé!",
        ListeningInteractionType.ThoughtBubble => "Em hãy chạm vào suy nghĩ hợp nhất với bạn trong tranh nhé!",
        _ => null
    };

    public static IReadOnlyList<ListeningAudioPart> For(
        ListeningInteraction interaction, IReadOnlyList<ListeningInteractionOption> options)
    {
        var parts = new List<ListeningAudioPart>();
        void Add(int? optionId, string key, string? value, bool required = true)
        {
            if (!string.IsNullOrWhiteSpace(value)) parts.Add(new ListeningAudioPart(optionId, key, value, required));
        }

        Add(null, Instruction, InstructionFor(interaction), required: ListeningAgeSets.HasOwnSet(interaction.TargetAge));
        switch (interaction.Type)
        {
            case ListeningInteractionType.ReadAloud:
                Add(null, Text, interaction.SourceQuote);
                Add(null, Match, interaction.MatchFeedback);
                break;
            case ListeningInteractionType.Checkpoint:
                Add(null, Prompt, interaction.Prompt);
                Add(null, NoMatch, interaction.NoMatchFeedback);
                if (interaction.Kind == CheckpointKind.Literal) Add(null, RetryLook, FixedLine(RetryLook));
                break;
            case ListeningInteractionType.RolePlay:
                if (interaction.TargetAge == 6) Add(null, Text, interaction.SourceQuote);
                Add(null, Match, interaction.MatchFeedback);
                if (interaction.TargetAge == 6) Add(null, Unclear, FixedLine(Unclear));
                break;
            case ListeningInteractionType.CharacterLetter:
                Add(null, Name, interaction.CharacterName);
                Add(null, Match, interaction.MatchFeedback);
                Add(null, RetryArrow, FixedLine(RetryArrow));
                Add(null, Unclear, FixedLine(Unclear));
                foreach (var option in options.Where(x => x.ListeningInteractionId == interaction.Id && !x.IsDeleted).OrderBy(x => x.SortOrder))
                    Add(option.Id, Sound, VietnameseLetterUnits.SoundOf(option.Text.Trim()));
                return parts;
            case ListeningInteractionType.WhoSaidIt:
                Add(null, Text, interaction.SourceQuote);
                Add(null, Match, interaction.MatchFeedback);
                Add(null, NoMatch, interaction.NoMatchFeedback);
                break;
            case ListeningInteractionType.FillBlank:
                // Đọc nguyên câu ngắn (câu trích ≤ 15 từ) để bé nghe lại câu rồi điền, không phải nghe lại cả đoạn.
                Add(null, Text, interaction.SourceQuote);
                Add(null, Hint, interaction.Hint);
                Add(null, Match, interaction.MatchFeedback);
                Add(null, NoMatch, interaction.NoMatchFeedback);
                break;
            case ListeningInteractionType.EventOrdering:
                Add(null, Match, interaction.MatchFeedback);
                Add(null, NoMatch, interaction.NoMatchFeedback);
                break;
            case ListeningInteractionType.PicturePrediction:
                Add(null, Prompt, interaction.Prompt);
                Add(null, Wait, interaction.WaitFeedback);
                break;
            case ListeningInteractionType.PictureSearch:
                Add(null, Prompt, interaction.Prompt);
                Add(null, Match, interaction.MatchFeedback);
                Add(null, NoMatch, interaction.NoMatchFeedback);
                Add(null, RetryLookClose, FixedLine(RetryLookClose));
                break;
            case ListeningInteractionType.TrueFalse:
                Add(null, Match, interaction.MatchFeedback);
                Add(null, NoMatch, interaction.NoMatchFeedback);
                Add(null, RetryTry, FixedLine(RetryTry));
                // Sai lần 2 thì nói đáp án của câu: câu này đúng hay không với truyện.
                Add(null, TruthYes, FixedLine(TruthYes));
                Add(null, TruthNo, FixedLine(TruthNo));
                break;
            case ListeningInteractionType.FollowAction:
                Add(null, Prompt, interaction.Prompt);
                Add(null, Match, interaction.MatchFeedback);
                Add(null, NoMatch, interaction.NoMatchFeedback);
                Add(null, RetryTry, FixedLine(RetryTry));
                break;
            case ListeningInteractionType.SentenceBuilder:
                Add(null, Match, interaction.MatchFeedback);
                Add(null, NoMatch, interaction.NoMatchFeedback);
                Add(null, RetryHear, FixedLine(RetryHear));
                var orders = SentenceOrders.Parse(interaction.AcceptedOrdersJson);
                var cards = options.Where(x => x.ListeningInteractionId == interaction.Id && !x.IsDeleted)
                    .Where(x => x.CorrectOrder.HasValue)
                    .GroupBy(x => x.CorrectOrder!.Value)
                    .ToDictionary(x => x.Key, x => x.First().Text.Trim());
                if (orders is not null)
                    for (var i = 0; i < orders.Count; i++)
                    {
                        var sentence = i == 0 ? interaction.SourceQuote :
                            string.Join(' ', orders[i].Select(position => cards.GetValueOrDefault(position)).Where(x => x is not null));
                        Add(null, SentenceKey(i), sentence);
                    }
                break;
            case ListeningInteractionType.AlternateReading:
                Add(null, Match, interaction.MatchFeedback);
                Add(null, Unclear, FixedLine(Unclear));
                break;
            case ListeningInteractionType.InferenceQuestion:
                Add(null, Text, interaction.SourceQuote);
                Add(null, Prompt, interaction.Prompt);
                Add(null, Match, interaction.MatchFeedback);
                Add(null, Explanation, interaction.Explanation);
                Add(null, RetryLookClose, FixedLine(RetryLookClose));
                break;
            case ListeningInteractionType.CauseEffect:
                Add(null, Match, interaction.MatchFeedback);
                Add(null, RetryTry, FixedLine(RetryTry));
                var pairCards = options.Where(x => x.ListeningInteractionId == interaction.Id && !x.IsDeleted).ToList();
                foreach (var effect in pairCards.Where(x => x.Role == ListeningOptionRole.Item).OrderBy(x => x.SortOrder))
                {
                    var cause = pairCards.FirstOrDefault(x => CausePairs.Matches(effect, x));
                    if (cause is not null) Add(effect.Id, Chain, CausePairs.Sentence(cause.Text, effect.Text));
                }
                break;
            case ListeningInteractionType.SceneTitle:
                Add(null, Prompt, interaction.Prompt);
                Add(null, Match, interaction.MatchFeedback);
                Add(null, Explanation, interaction.Explanation);
                Add(null, RetryLookClose, FixedLine(RetryLookClose));
                break;
            case ListeningInteractionType.ThoughtBubble:
                Add(null, Prompt, interaction.Prompt);
                Add(null, Match, interaction.MatchFeedback);
                Add(null, RetryLook, FixedLine(RetryLook));
                break;
        }
        // RolePlay dùng audio segment; đáp án chấp nhận của FillBlank không đọc riêng; ReadAloud không có option.
        if (interaction.Type is ListeningInteractionType.RolePlay or ListeningInteractionType.FillBlank or ListeningInteractionType.ReadAloud) return parts;

        var readsFeedback = interaction.Type is ListeningInteractionType.Checkpoint or ListeningInteractionType.PicturePrediction or ListeningInteractionType.SceneTitle;
        foreach (var option in options.Where(x => x.ListeningInteractionId == interaction.Id && !x.IsDeleted)
                     .OrderBy(x => x.SortOrder))
        {
            if (option.Role == ListeningOptionRole.Clue) continue;   // manh mối chỉ để tô sáng đoạn, không đọc riêng
            Add(option.Id, Text, option.Text);
            if (readsFeedback) Add(option.Id, Feedback, option.Feedback);
        }
        return parts;
    }
}
