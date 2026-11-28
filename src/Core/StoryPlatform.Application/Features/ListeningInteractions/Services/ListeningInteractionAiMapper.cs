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
        IReadOnlyList<ListeningValidationError> previousErrors, int? targetAge = null, CheckpointKind? kind = null) => new()
    {
        RequestId = requestId,
        TargetAge = targetAge,
        CheckpointKind = kind?.ToString(),
        AllowedLetters = type == ListeningInteractionType.CharacterLetter ? VietnameseLetterUnits.BaseLetters.ToArray() : [],
        InteractionType = type.ToString(),
        Count = count,
        AgeBand = ageBand.ToString(),
        Language = language,
        Story = new ListeningStoryInfoDto { Title = title, Lesson = lesson },
        Blocks = blocks.Select(b => new ListeningBlockDto(b.BlockId, b.Text.TrimEnd())).ToArray(),
        LastBlockId = blocks.Count == 0 ? string.Empty : blocks[^1].BlockId,
        OccupiedBlockIds = blocks.Where(b => occupiedAnchorOffsets.Contains(b.EndOffset)).Select(b => b.BlockId).ToArray(),
        // Từ vựng gợi ý chọn câu cho FillBlank và ReadAloud, đoạn có thoại cho RolePlay.
        VocabularyTerms = type is ListeningInteractionType.FillBlank or ListeningInteractionType.ReadAloud ? vocabularyTerms.ToArray() : [],
        DialogueBlockIds = type is ListeningInteractionType.RolePlay or ListeningInteractionType.WhoSaidIt or ListeningInteractionType.AlternateReading ? dialogueBlockIds.ToArray() : [],
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
                Text = o.Text ?? string.Empty, Feedback = o.Feedback, IsMatch = o.IsMatch,
                ImageDescription = ParseKind(x.Kind) == CheckpointKind.Literal ? o.ImageDescription : null
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
            Type = type, AnchorBlockId = x.AfterBlockId ?? string.Empty,
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
        ListeningInteractionType.ReadAloud => (response.ReadAloudPassages ?? []).Select(x => new ListeningInteractionDraft
        {
            Type = type,
            SourceBlockId = x.BlockId,
            SourceQuote = x.PassageQuote,
            MatchFeedback = x.PraiseFeedback
        }).ToArray(),
        ListeningInteractionType.CharacterLetter => (response.CharacterLetters ?? []).Select(x => new ListeningInteractionDraft
        {
            Type = type, AnchorBlockId = x.AfterBlockId ?? string.Empty, CharacterName = x.CharacterName,
            BlankIndex = x.BlankIndex, MatchFeedback = x.PraiseFeedback,
            Options = (x.Options ?? []).Select(o => new ListeningOptionDraft { Text = o.Text ?? string.Empty, IsMatch = o.IsMatch, ImageDescription = o.IsMatch ? x.ImageDescription : null }).ToArray()
        }).ToArray(),
        ListeningInteractionType.WhoSaidIt => (response.WhoSaidIts ?? []).Select(x => new ListeningInteractionDraft
        {
            Type = type, AnchorBlockId = x.AfterBlockId ?? string.Empty, SourceQuote = x.SourceQuote ?? x.LineQuote,
            MatchFeedback = x.MatchFeedback, NoMatchFeedback = x.RetryFeedback,
            Options = (x.Speakers ?? []).Select(o => new ListeningOptionDraft { Text = o.Name, IsMatch = o.IsMatch, ImageDescription = o.ImageDescription }).ToArray()
        }).ToArray(),
        ListeningInteractionType.PictureSearch => (response.PictureSearches ?? []).Select(x => new ListeningInteractionDraft
        {
            Type = type, AnchorBlockId = x.AfterBlockId ?? string.Empty, SourceQuote = x.SourceQuote,
            TargetLabel = x.TargetLabel, Prompt = x.Question, MatchFeedback = x.MatchFeedback,
            NoMatchFeedback = x.RetryFeedback
        }).ToArray(),
        ListeningInteractionType.TrueFalse => (response.TrueFalses ?? []).Select(x => new ListeningInteractionDraft
        {
            Type = type, AnchorBlockId = x.AfterBlockId ?? string.Empty,
            MatchFeedback = x.MatchFeedback, NoMatchFeedback = x.RetryFeedback,
            Options = (x.Statements ?? []).Select(s => new ListeningOptionDraft
            { Text = s.Text, IsMatch = s.IsTrue, ImageDescription = s.EvidenceLabel }).ToArray()
        }).ToArray(),
        ListeningInteractionType.FollowAction => (response.FollowActions ?? []).Select(x => new ListeningInteractionDraft
        {
            Type = type, AnchorBlockId = x.AfterBlockId ?? string.Empty, CharacterName = x.CharacterName,
            Prompt = x.Question, MatchFeedback = x.MatchFeedback, NoMatchFeedback = x.RetryFeedback,
            Options = (x.Items ?? []).Select(s => new ListeningOptionDraft
            { Text = s.Name, IsMatch = s.IsCorrect, ImageDescription = s.ImageDescription, Role = ListeningOptionRole.Item })
                .Concat(x.Target is null ? [] : [new ListeningOptionDraft
                { Text = x.Target.Name, ImageDescription = x.Target.ImageDescription, Role = ListeningOptionRole.Target }]).ToArray()
        }).ToArray(),
        ListeningInteractionType.SentenceBuilder => (response.SentenceBuilders ?? []).Select(x => new ListeningInteractionDraft
        {
            Type = type, AnchorBlockId = x.AfterBlockId ?? string.Empty, SourceQuote = x.SentenceQuote,
            AcceptedOrdersJson = System.Text.Json.JsonSerializer.Serialize(x.AcceptedOrders),
            MatchFeedback = x.MatchFeedback, NoMatchFeedback = x.RetryFeedback,
            Options = (x.Cards ?? []).Select((card, index) => new ListeningOptionDraft
            { Text = card, CorrectOrder = index + 1 }).ToArray()
        }).ToArray(),
        ListeningInteractionType.AlternateReading => (response.AlternateReadings ?? []).Select(x => new ListeningInteractionDraft
        {
            Type = type, AnchorBlockId = x.AfterBlockId ?? string.Empty, MatchFeedback = x.PraiseFeedback,
            Options = (x.Lines ?? []).Select((line, index) => new ListeningOptionDraft
            { Text = line.Text ?? string.Empty, Speaker = line.Speaker, CorrectOrder = index + 1 }).ToArray()
        }).ToArray(),
        ListeningInteractionType.InferenceQuestion => (response.InferenceQuestions ?? []).Select(x => new ListeningInteractionDraft
        {
            Type = type, AnchorBlockId = x.AfterBlockId ?? string.Empty, SourceQuote = x.Passage, Prompt = x.Question,
            Explanation = x.Explanation, MatchFeedback = x.MatchFeedback,
            Options = (x.Answers ?? []).Select(a => new ListeningOptionDraft { Text = a.Text ?? string.Empty, IsMatch = a.IsCorrect })
                .Concat((x.Clues ?? []).Select(c => new ListeningOptionDraft { Text = c ?? string.Empty, Role = ListeningOptionRole.Clue })).ToArray()
        }).ToArray(),
        ListeningInteractionType.CauseEffect => (response.CauseEffects ?? []).Select(x => new ListeningInteractionDraft
        {
            Type = type, AnchorBlockId = x.AfterBlockId ?? string.Empty, MatchFeedback = x.MatchFeedback,
            Options = (x.Pairs ?? []).Select((p, index) => new ListeningOptionDraft
                { Text = p.Effect?.Text ?? string.Empty, SourceQuote = p.Effect?.SourceQuote, Role = ListeningOptionRole.Item, CorrectOrder = index + 1 })
                .Concat((x.Pairs ?? []).Select((p, index) => new ListeningOptionDraft
                { Text = p.Cause?.Text ?? string.Empty, SourceQuote = p.Cause?.SourceQuote, Role = ListeningOptionRole.Target, CorrectOrder = index + 1 })).ToArray()
        }).ToArray(),
        ListeningInteractionType.SceneTitle => (response.SceneTitles ?? []).Select(x => new ListeningInteractionDraft
        {
            Type = type, AnchorBlockId = x.AfterBlockId ?? string.Empty, Explanation = x.Explanation, MatchFeedback = x.MatchFeedback,
            Options = (x.Titles ?? []).Select(t => new ListeningOptionDraft { Text = t.Text ?? string.Empty, IsMatch = t.IsCorrect, Feedback = t.Feedback }).ToArray()
        }).ToArray(),
        ListeningInteractionType.ThoughtBubble => (response.ThoughtBubbles ?? []).Select(x => new ListeningInteractionDraft
        {
            Type = type, AnchorBlockId = x.AfterBlockId ?? string.Empty, CharacterName = x.CharacterName, Prompt = x.Question,
            SourceQuote = x.SourceQuote, MatchFeedback = x.MatchFeedback,
            Options = (x.Bubbles ?? []).Select(b => new ListeningOptionDraft { Text = b.Text ?? string.Empty, IsMatch = b.IsCorrect, Emotion = b.Emotion }).ToArray()
        }).ToArray(),
        _ => []
    };

    // Chỉ nhận đúng tên kiểu ("Emotion"), không nhận số; sai thì để null cho rules báo SCHEMA_INVALID.
    private static CheckpointKind? ParseKind(string? kind) =>
        Enum.GetValues<CheckpointKind>().Cast<CheckpointKind?>()
            .FirstOrDefault(k => string.Equals(k.ToString(), kind, StringComparison.OrdinalIgnoreCase));
}
