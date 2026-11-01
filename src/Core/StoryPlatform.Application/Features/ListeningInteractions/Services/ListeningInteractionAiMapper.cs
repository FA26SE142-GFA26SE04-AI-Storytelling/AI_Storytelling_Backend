using StoryPlatform.Application.Features.ListeningInteractions.Models;
using StoryPlatform.Application.Features.MediaGeneration.Models;
using StoryPlatform.Contracts.AI.Models;
using StoryPlatform.Contracts.AI.Requests;
using StoryPlatform.Contracts.AI.Responses;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ListeningInteractions.Services;

/// <summary>
/// Đổi kế hoạch thành request cho AI và đổi response về draft để mapper và rules P1 xử lý.
/// Không kiểm tra gì ở đây: trường thiếu để null, rules sẽ báo lỗi.
/// </summary>
public static class ListeningInteractionAiMapper
{
    public static GenerateListeningInteractionRequest BuildRequest(
        string requestId, ListeningInteractionType type, int count, AgeBand ageBand, string language, string title, string lesson,
        IReadOnlyList<StoryTextBlock> blocks, IReadOnlyCollection<string> dialogueBlockIds, IReadOnlyCollection<int> occupiedAnchorOffsets,
        IReadOnlyList<IReadOnlyList<string>> allowedBlockIds, IReadOnlyList<string> vocabularyTerms,
        IReadOnlyList<ListeningValidationError> previousErrors) => new()
    {
        RequestId = requestId,
        InteractionType = type.ToString(),
        Count = count,
        AgeBand = ageBand.ToString(),
        Language = language,
        Story = new ListeningStoryInfoDto { Title = title, Lesson = lesson },
        Blocks = blocks.Select(b => new ListeningBlockDto(b.BlockId, b.Text.TrimEnd())).ToArray(),
        LastBlockId = blocks.Count == 0 ? string.Empty : blocks[^1].BlockId,
        OccupiedBlockIds = blocks.Where(b => occupiedAnchorOffsets.Contains(b.EndOffset)).Select(b => b.BlockId).ToArray(),
        // Chỉ loại cần mới gửi: từ vựng cho FillBlank, đoạn có thoại cho RolePlay.
        VocabularyTerms = type == ListeningInteractionType.FillBlank ? vocabularyTerms.ToArray() : [],
        DialogueBlockIds = type == ListeningInteractionType.RolePlay ? dialogueBlockIds.ToArray() : [],
        AllowedBlockIds = allowedBlockIds,
        PreviousErrors = previousErrors.Select(e => new ListeningPreviousErrorDto(e.Code, e.ItemIndex, e.Field)).ToArray()
    };

    public static IReadOnlyList<ListeningInteractionDraft> ToDrafts(GenerateListeningInteractionResponse response, ListeningInteractionType type) => type switch
    {
        ListeningInteractionType.Checkpoint => (response.Checkpoints ?? []).Select(x => new ListeningInteractionDraft
        {
            Type = type,
            AnchorBlockId = x.AfterBlockId ?? string.Empty,
            Kind = ParseKind(x.Kind),
            Prompt = x.Question,
            SourceQuote = x.SourceQuote,
            Explanation = x.Explanation,
            NoMatchFeedback = x.OtherFeedback,
            Options = (x.Options ?? []).Select(o => new ListeningOptionDraft
            {
                Text = o.Text ?? string.Empty, Feedback = o.Feedback, IsMatch = o.IsMatch
            }).ToArray()
        }).ToArray(),
        ListeningInteractionType.RolePlay => (response.RolePlays ?? []).Select(x => new ListeningInteractionDraft
        {
            Type = type,
            AnchorBlockId = x.AfterBlockId ?? string.Empty,
            CharacterName = x.CharacterName,
            SourceQuote = x.LineQuote,
            MatchFeedback = x.PraiseFeedback
        }).ToArray(),
        ListeningInteractionType.FillBlank => (response.FillBlanks ?? []).Select(x => new ListeningInteractionDraft
        {
            Type = type,
            AnchorBlockId = x.AfterBlockId ?? string.Empty,
            SourceQuote = x.SentenceQuote,
            HiddenWord = x.HiddenWord,
            Hint = x.Hint,
            MatchFeedback = x.MatchFeedback,
            NoMatchFeedback = x.SuggestFeedback,
            Options = (x.AcceptedAnswers ?? []).Select(a => new ListeningOptionDraft { Text = a ?? string.Empty }).ToArray()
        }).ToArray(),
        ListeningInteractionType.EventOrdering => (response.EventOrderings ?? []).Select(x => new ListeningInteractionDraft
        {
            Type = type, // AnchorBlockId để trống: mapper P1 gán đoạn cuối
            MatchFeedback = x.MatchFeedback,
            NoMatchFeedback = x.RetryFeedback,
            Options = (x.Events ?? []).Select(e => new ListeningOptionDraft
            {
                Text = e.Text ?? string.Empty, CorrectOrder = e.Order, BlockId = e.BlockId, SourceQuote = e.SourceQuote
            }).ToArray()
        }).ToArray(),
        ListeningInteractionType.PicturePrediction => (response.PicturePredictions ?? []).Select(x => new ListeningInteractionDraft
        {
            Type = type,
            AnchorBlockId = x.AfterBlockId ?? string.Empty,
            RevealBlockId = x.RevealBlockId,
            Prompt = x.Question,
            SourceQuote = x.SourceQuote,
            WaitFeedback = x.WaitFeedback,
            Options = (x.Options ?? []).Select(o => new ListeningOptionDraft
            {
                Text = o.Label ?? string.Empty, ImageDescription = o.ImageDescription, Feedback = o.Feedback, IsMatch = o.IsMatch
            }).ToArray()
        }).ToArray(),
        _ => []
    };

    // Chỉ nhận đúng tên kiểu ("Emotion"), không nhận số; sai thì để null cho rules báo SCHEMA_INVALID.
    private static CheckpointKind? ParseKind(string? kind) =>
        Enum.GetValues<CheckpointKind>().Cast<CheckpointKind?>()
            .FirstOrDefault(k => string.Equals(k.ToString(), kind, StringComparison.OrdinalIgnoreCase));
}
