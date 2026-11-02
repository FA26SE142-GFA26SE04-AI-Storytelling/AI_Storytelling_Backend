using StoryPlatform.Application.Features.ListeningInteractions.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ReadingSessions;

/// <summary>
/// Dựng tương tác lúc nghe cho từng scene (spec 8.1). Chỉ lấy tương tác đã gắn scene và đủ media;
/// giấu mọi đáp án; xáo thẻ Sắp xếp cố định theo phiên.
/// </summary>
public static class ChildListeningContent
{
    public const string Blank = "____";
    /// <summary>Xếp sai tới lần này trong một phiên thì hiện thứ tự đúng (chốt 02/11/2026).</summary>
    public const int RevealAfterWrongOrders = 3;

    public sealed record SceneInteraction(ReadingInteractionDto Interaction, bool AttemptedInSession);

    public static IReadOnlyDictionary<int, SceneInteraction> Build(int sessionId, IReadOnlyList<StoryScene> scenes,
        IReadOnlyList<StorySegment> segments, IReadOnlyList<ListeningInteraction> interactions,
        IReadOnlyList<ListeningInteractionOption> options, IReadOnlyList<MediaAsset> assets,
        IReadOnlyList<ListeningInteractionAttempt> sessionAttempts)
    {
        var result = new Dictionary<int, SceneInteraction>();
        foreach (var interaction in interactions.Where(x => !x.IsDeleted && x.StorySceneId.HasValue).OrderBy(x => x.AnchorOffset))
        {
            if (scenes.All(scene => scene.Id != interaction.StorySceneId)) continue;
            if (ListeningMediaRequirements.Missing(interaction, options, assets).Count > 0) continue;
            string? modelAudio = null;
            if (interaction.Type == ListeningInteractionType.RolePlay)
            {
                modelAudio = ModelAudioUrl(interaction, scenes, segments, assets);
                if (modelAudio is null) continue;
            }

            var own = options.Where(x => x.ListeningInteractionId == interaction.Id && !x.IsDeleted)
                .OrderBy(x => x.SortOrder).ToList();
            result[interaction.StorySceneId!.Value] = new SceneInteraction(
                ToDto(sessionId, interaction, own, scenes, assets, modelAudio), IsAttempted(interaction, sessionAttempts));
        }

        return result;
    }

    /// <summary>EventOrdering chỉ coi là đã thử khi đã khớp hoặc đã sai đủ số lần để lộ đáp án.</summary>
    public static bool IsAttempted(ListeningInteraction interaction, IReadOnlyList<ListeningInteractionAttempt> sessionAttempts)
    {
        var mine = sessionAttempts.Where(x => x.ListeningInteractionId == interaction.Id && !x.IsDeleted).ToList();
        if (interaction.Type != ListeningInteractionType.EventOrdering) return mine.Count > 0;
        return mine.Any(x => x.IsMatch == true) || mine.Count(x => x.IsMatch == false) >= RevealAfterWrongOrders;
    }

    public static string? Mask(string? sentence, string? hiddenWord)
    {
        if (string.IsNullOrEmpty(sentence) || string.IsNullOrEmpty(hiddenWord)) return sentence;
        var index = sentence.IndexOf(hiddenWord, StringComparison.OrdinalIgnoreCase);
        return index < 0 ? sentence : string.Concat(sentence.AsSpan(0, index), Blank, sentence.AsSpan(index + hiddenWord.Length));
    }

    private static ReadingInteractionDto ToDto(int sessionId, ListeningInteraction interaction,
        IReadOnlyList<ListeningInteractionOption> own, IReadOnlyList<StoryScene> scenes,
        IReadOnlyList<MediaAsset> assets, string? modelAudio)
    {
        var dto = new ReadingInteractionDto
        {
            Id = interaction.Id,
            Type = interaction.Type.ToString(),
            Kind = interaction.Kind?.ToString(),
            Prompt = interaction.Prompt,
            CharacterName = interaction.CharacterName,
            ModelSentence = interaction.Type == ListeningInteractionType.RolePlay ? interaction.SourceQuote : null,
            ModelAudioUrl = modelAudio,
            MaskedSentence = interaction.Type == ListeningInteractionType.FillBlank
                ? Mask(interaction.SourceQuote, interaction.HiddenWord) : null,
            Hint = interaction.Hint,
            WaitFeedback = interaction.WaitFeedback,
            RevealSceneIndex = interaction.RevealOffset is int reveal
                ? scenes.FirstOrDefault(scene => scene.TextRangeEnd == reveal)?.SceneIndex : null
        };
        foreach (var key in new[] { ListeningAudioParts.Prompt, ListeningAudioParts.Wait, ListeningAudioParts.Match,
                     ListeningAudioParts.NoMatch, ListeningAudioParts.Hint })
            if (ListeningMediaRequirements.FindAudio(assets, interaction.Id, null, key)?.Url is { } url)
                dto.AudioUrls[key] = url;

        // Option của FillBlank là đáp án chấp nhận nên không bao giờ trả.
        if (interaction.Type is ListeningInteractionType.RolePlay or ListeningInteractionType.FillBlank) return dto;
        var withFeedback = interaction.Type is ListeningInteractionType.Checkpoint or ListeningInteractionType.PicturePrediction;
        var cards = own.Select(option => new ReadingInteractionOptionDto
        {
            Id = option.Id,
            Text = option.Text,
            ImageUrl = ListeningMediaRequirements.FindImage(assets, option.Id)?.Url,
            AudioUrl = ListeningMediaRequirements.FindAudio(assets, interaction.Id, option.Id, ListeningAudioParts.Text)?.Url,
            Feedback = withFeedback ? option.Feedback : null,
            FeedbackAudioUrl = withFeedback
                ? ListeningMediaRequirements.FindAudio(assets, interaction.Id, option.Id, ListeningAudioParts.Feedback)?.Url
                : null
        }).ToList();
        dto.Options = interaction.Type == ListeningInteractionType.EventOrdering
            ? Shuffle(sessionId, interaction.Id, cards, own) : cards;
        return dto;
    }

    // Xáo cố định theo phiên (mở lại vẫn cùng thứ tự) và không bao giờ trùng thứ tự đúng.
    private static List<ReadingInteractionOptionDto> Shuffle(int sessionId, int interactionId,
        List<ReadingInteractionOptionDto> cards, IReadOnlyList<ListeningInteractionOption> own)
    {
        var correct = own.OrderBy(x => x.CorrectOrder).Select(x => x.Id).ToList();
        var random = new Random(unchecked(sessionId * 7919 + interactionId));
        var shuffled = cards.OrderBy(_ => random.Next()).ToList();
        if (shuffled.Count > 1 && shuffled.Select(x => x.Id).SequenceEqual(correct))
            shuffled = shuffled.Skip(1).Append(shuffled[0]).ToList();
        return shuffled;
    }

    // Offset của segment tính trong SceneText; SourceStart tính trong cả truyện.
    private static string? ModelAudioUrl(ListeningInteraction interaction, IReadOnlyList<StoryScene> scenes,
        IReadOnlyList<StorySegment> segments, IReadOnlyList<MediaAsset> assets)
    {
        if (interaction.SourceStart is not int start) return null;
        var scene = scenes.FirstOrDefault(x => x.TextRangeStart <= start && start < x.TextRangeEnd);
        if (scene is null) return null;
        var segment = segments.FirstOrDefault(x => x.StorySceneId == scene.Id && !x.IsDeleted
            && scene.TextRangeStart + x.StartOffset <= start && start < scene.TextRangeStart + x.EndOffset);
        return segment is null ? null : assets.FirstOrDefault(asset => asset.Type == MediaType.TtsAudio
            && asset.StorySegmentId == segment.Id && asset.Status == MediaStatus.Ready && asset.IsCurrent
            && !asset.IsDeleted && !string.IsNullOrWhiteSpace(asset.Url))?.Url;
    }
}
