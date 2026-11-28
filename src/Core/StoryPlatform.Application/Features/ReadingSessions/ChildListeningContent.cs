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
    public const int MaxChoiceAttempts = 2;
    /// <summary>Xếp sai tới lần này trong một phiên thì hiện thứ tự đúng (chốt 02/11/2026).</summary>
    public const int RevealAfterWrongOrders = 3;
    public static int RevealAfterWrongOrdersFor(int? age) => age == 7 ? 2 : RevealAfterWrongOrders;

    public sealed record SceneInteraction(ReadingInteractionDto Interaction, bool AttemptedInSession);

    public static IReadOnlyDictionary<int, SceneInteraction> Build(int sessionId, int childAge, IReadOnlyList<StoryScene> scenes,
        IReadOnlyList<StorySegment> segments, IReadOnlyList<ListeningInteraction> interactions,
        IReadOnlyList<ListeningInteractionOption> options, IReadOnlyList<MediaAsset> assets,
        IReadOnlyList<ListeningInteractionAttempt> sessionAttempts, IReadOnlyList<IllustrationBeat>? beats = null)
    {
        beats ??= [];
        var result = new Dictionary<int, SceneInteraction>();
        foreach (var interaction in interactions.Where(x => !x.IsDeleted && x.TargetAge == childAge && x.StorySceneId.HasValue && x.Type != ListeningInteractionType.ReadAloud).OrderBy(x => x.AnchorOffset))
        {
            if (scenes.All(scene => scene.Id != interaction.StorySceneId)) continue;
            if (ListeningMediaRequirements.Missing(interaction, options, assets).Count > 0) continue;
            string? modelAudio = null;
            if (interaction.Type == ListeningInteractionType.RolePlay)
            {
                modelAudio = interaction.TargetAge == 6
                    ? ListeningMediaRequirements.FindAudio(assets, interaction.Id, null, ListeningAudioParts.Text)?.Url
                    : ModelAudioUrl(interaction, scenes, segments, assets);
                if (modelAudio is null) continue;
            }

            var own = options.Where(x => x.ListeningInteractionId == interaction.Id && !x.IsDeleted)
                .OrderBy(x => x.SortOrder).ToList();
            if (interaction.Type == ListeningInteractionType.PictureSearch)
            {
                var box = HotspotBox.TryParse(interaction.HotspotJson);
                var picture = StoryPicture(interaction.SourceStart, scenes, beats, assets, true);
                if (box is null || picture is null || box.AssetId != picture.Id) continue;
            }
            if (interaction.Type == ListeningInteractionType.ThoughtBubble &&
                StoryPicture(interaction.SourceStart, scenes, beats, assets, true) is null) continue;
            // Manh mối phải còn nằm trong đoạn (người duyệt có thể đã sửa đoạn); nếu không thì ẩn thay vì tô sai chỗ.
            if (interaction.Type == ListeningInteractionType.InferenceQuestion && (string.IsNullOrWhiteSpace(interaction.SourceQuote) ||
                ClueSpans.Find(interaction.SourceQuote, own.Where(x => x.Role == ListeningOptionRole.Clue).Select(x => x.Text).ToArray()) is null)) continue;
            result[interaction.StorySceneId!.Value] = new SceneInteraction(
                ToDto(sessionId, interaction, own, scenes, beats, assets, modelAudio), IsAttempted(interaction, sessionAttempts, own));
        }

        return result;
    }

    /// <summary>Đoạn luyện đọc to đã gắn scene và đủ audio (câu mẫu + lời khen); null nếu chưa có.</summary>
    public static ReadAloudPracticeDto? BuildReadAloud(int childAge, IReadOnlyList<ListeningInteraction> interactions, IReadOnlyList<MediaAsset> assets)
    {
        var interaction = interactions
            .Where(x => !x.IsDeleted && x.TargetAge == childAge && x.Type == ListeningInteractionType.ReadAloud && x.StorySceneId.HasValue
                        && !string.IsNullOrWhiteSpace(x.SourceQuote) && !string.IsNullOrWhiteSpace(x.MatchFeedback))
            .OrderByDescending(x => x.Id)
            .FirstOrDefault();
        if (interaction is null || ListeningMediaRequirements.Missing(interaction, [], assets).Count > 0) return null;
        return new ReadAloudPracticeDto
        {
            InteractionId = interaction.Id,
            Text = interaction.SourceQuote!,
            AudioUrl = ListeningMediaRequirements.FindAudio(assets, interaction.Id, null, ListeningAudioParts.Text)?.Url,
            PraiseFeedback = interaction.MatchFeedback,
            PraiseAudioUrl = ListeningMediaRequirements.FindAudio(assets, interaction.Id, null, ListeningAudioParts.Match)?.Url
        };
    }

    /// <summary>EventOrdering chỉ coi là đã thử khi đã khớp hoặc đã sai đủ số lần để lộ đáp án.</summary>
    public static bool IsAttempted(ListeningInteraction interaction, IReadOnlyList<ListeningInteractionAttempt> sessionAttempts,
        IReadOnlyList<ListeningInteractionOption>? options = null)
    {
        var mine = sessionAttempts.Where(x => x.ListeningInteractionId == interaction.Id && !x.IsDeleted).ToList();
        // Nối nguyên nhân: xong khi cả 3 thẻ kết quả đã nối đúng hoặc đã thử đủ số lần (hệ thống nối sẵn).
        if (interaction.Type == ListeningInteractionType.CauseEffect)
        {
            var effects = (options ?? []).Where(x => x.ListeningInteractionId == interaction.Id && !x.IsDeleted && x.Role == ListeningOptionRole.Item).ToList();
            return effects.Count > 0 && effects.All(effect =>
            {
                var tries = mine.Where(x => x.SelectedOptionId == effect.Id).ToList();
                return tries.Any(x => x.IsMatch == true) || tries.Count >= MaxChoiceAttempts;
            });
        }
        if (interaction.Type == ListeningInteractionType.TrueFalse)
            return mine.GroupBy(x => x.SelectedOptionId).Count(group =>
                group.Any(x => x.IsMatch == true) || group.Count() >= MaxChoiceAttempts) >= 2;
        if (AllowsRetry(interaction)) return mine.Any(x => x.IsMatch == true) || mine.Count >= MaxChoiceAttempts;
        if (interaction.Type != ListeningInteractionType.EventOrdering) return mine.Count > 0;
        return mine.Any(x => x.IsMatch == true) || mine.Count(x => x.IsMatch == false) >= RevealAfterWrongOrdersFor(interaction.TargetAge);
    }

    public static bool AllowsRetry(ListeningInteraction interaction) =>
        interaction.Type is ListeningInteractionType.CharacterLetter or ListeningInteractionType.WhoSaidIt or
            ListeningInteractionType.PictureSearch or ListeningInteractionType.TrueFalse or
            ListeningInteractionType.FollowAction or ListeningInteractionType.SentenceBuilder or
            ListeningInteractionType.InferenceQuestion or ListeningInteractionType.SceneTitle or
            ListeningInteractionType.ThoughtBubble ||
        interaction.Type == ListeningInteractionType.Checkpoint && interaction.Kind == CheckpointKind.Literal;

    public static string? Mask(string? sentence, string? hiddenWord)
    {
        if (string.IsNullOrEmpty(sentence) || string.IsNullOrEmpty(hiddenWord)) return sentence;
        var index = sentence.IndexOf(hiddenWord, StringComparison.OrdinalIgnoreCase);
        return index < 0 ? sentence : string.Concat(sentence.AsSpan(0, index), Blank, sentence.AsSpan(index + hiddenWord.Length));
    }

    private static ReadingInteractionDto ToDto(int sessionId, ListeningInteraction interaction,
        IReadOnlyList<ListeningInteractionOption> own, IReadOnlyList<StoryScene> scenes,
        IReadOnlyList<IllustrationBeat> beats, IReadOnlyList<MediaAsset> assets, string? modelAudio)
    {
        var dto = new ReadingInteractionDto
        {
            Id = interaction.Id,
            TargetAge = interaction.TargetAge,
            // Nối nguyên nhân: mỗi thẻ kết quả được nối tối đa 2 lần.
            MaxChoices = AllowsRetry(interaction) || interaction.Type == ListeningInteractionType.CauseEffect ? MaxChoiceAttempts : 1,
            Graded = !(interaction.Type == ListeningInteractionType.PicturePrediction && interaction.TargetAge == 6),
            MaxVoiceRetries = interaction.TargetAge == 6 && interaction.Type is ListeningInteractionType.RolePlay or ListeningInteractionType.CharacterLetter
                || interaction.TargetAge == 7 && interaction.Type == ListeningInteractionType.SentenceBuilder
                || interaction.TargetAge == 8 && interaction.Type == ListeningInteractionType.AlternateReading
                ? 2 : null,
            Type = interaction.Type.ToString(),
            Kind = interaction.Kind?.ToString(),
            Prompt = interaction.Prompt,
            Instruction = ListeningAudioParts.InstructionFor(interaction),
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
        foreach (var key in new[] { ListeningAudioParts.Prompt, ListeningAudioParts.Instruction, ListeningAudioParts.Wait,
                     ListeningAudioParts.Match, ListeningAudioParts.NoMatch, ListeningAudioParts.Hint, ListeningAudioParts.Text,
                     ListeningAudioParts.Name, ListeningAudioParts.RetryLook, ListeningAudioParts.RetryArrow,
                     ListeningAudioParts.RetryLookClose, ListeningAudioParts.RetryHear, ListeningAudioParts.RetryTry,
                     ListeningAudioParts.Unclear, ListeningAudioParts.Explanation })
        {
            var audio = ListeningMediaRequirements.FindAudio(assets, interaction.Id, null, key);
            if (audio?.Url is { } url) dto.AudioUrls[key] = url;
            if (ListeningAudioParts.HighlightKeys.Contains(key) && !string.IsNullOrWhiteSpace(audio?.WordTimings))
                dto.AudioWordTimings[key] = audio.WordTimings;
        }

        if (interaction.Type == ListeningInteractionType.PictureSearch)
        {
            dto.SearchImageUrl = StoryPicture(interaction.SourceStart, scenes, beats, assets, true)?.Url;
            dto.SearchTarget = interaction.TargetLabel;
            return dto;
        }
        if (interaction.Type == ListeningInteractionType.TrueFalse)
        {
            dto.Statements = ShuffleItems(sessionId, interaction.Id, own).Select(option => new ReadingStatementDto
            {
                Id = option.Id, Text = option.Text,
                AudioUrl = ListeningMediaRequirements.FindAudio(assets, interaction.Id, option.Id, ListeningAudioParts.Text)?.Url
            }).ToList();
            return dto;
        }
        if (interaction.Type == ListeningInteractionType.FollowAction)
        {
            dto.ActionItems = own.Where(option => option.Role == ListeningOptionRole.Item).Select(option => new ReadingActionItemDto
            {
                Id = option.Id, Name = option.Text,
                ImageUrl = ListeningMediaRequirements.FindImage(assets, option.Id)?.Url,
                AudioUrl = ListeningMediaRequirements.FindAudio(assets, interaction.Id, option.Id, ListeningAudioParts.Text)?.Url
            }).ToList();
            var target = own.FirstOrDefault(option => option.Role == ListeningOptionRole.Target);
            if (target is not null) dto.ActionTarget = new ReadingActionItemDto
            {
                Id = target.Id, Name = target.Text,
                ImageUrl = ListeningMediaRequirements.FindImage(assets, target.Id)?.Url,
                AudioUrl = ListeningMediaRequirements.FindAudio(assets, interaction.Id, target.Id, ListeningAudioParts.Text)?.Url
            };
            return dto;
        }
        if (interaction.Type == ListeningInteractionType.SentenceBuilder)
        {
            dto.WordCards = ShuffleItems(sessionId, interaction.Id, own, true).Select(option => new ReadingWordCardDto
            {
                Id = option.Id, Text = option.Text,
                AudioUrl = ListeningMediaRequirements.FindAudio(assets, interaction.Id, option.Id, ListeningAudioParts.Text)?.Url
            }).ToList();
            dto.SlotCount = own.Count;
            return dto;
        }

        if (interaction.Type == ListeningInteractionType.AlternateReading)
        {
            var lines = own.OrderBy(option => option.CorrectOrder).ToList();
            dto.Characters = lines.Select(line => line.Speaker ?? string.Empty).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            dto.Lines = lines.Select(line =>
            {
                var audio = ListeningMediaRequirements.FindAudio(assets, interaction.Id, line.Id, ListeningAudioParts.Text);
                return new ReadingLineDto
                {
                    Id = line.Id, Speaker = line.Speaker ?? string.Empty, Text = line.Text, AudioUrl = audio?.Url,
                    WordTimings = string.IsNullOrWhiteSpace(audio?.WordTimings) ? null : audio.WordTimings
                };
            }).ToList();
            return dto;
        }
        if (interaction.Type == ListeningInteractionType.CauseEffect)
        {
            ReadingPairCardDto Card(ListeningInteractionOption option) => new()
            {
                Id = option.Id, Text = option.Text,
                AudioUrl = ListeningMediaRequirements.FindAudio(assets, interaction.Id, option.Id, ListeningAudioParts.Text)?.Url
            };
            dto.Effects = ShuffleItems(sessionId, interaction.Id, own.Where(option => option.Role == ListeningOptionRole.Item).ToList()).Select(Card).ToList();
            dto.Causes = ShuffleItems(sessionId, interaction.Id + 1, own.Where(option => option.Role == ListeningOptionRole.Target).ToList()).Select(Card).ToList();
            return dto;
        }
        if (interaction.Type == ListeningInteractionType.InferenceQuestion)
        {
            dto.Passage = interaction.SourceQuote;
            own = own.Where(option => option.Role == ListeningOptionRole.None).ToList();
        }
        if (interaction.Type is ListeningInteractionType.SceneTitle or ListeningInteractionType.ThoughtBubble)
            dto.SceneImageUrl = (interaction.Type == ListeningInteractionType.ThoughtBubble
                ? StoryPicture(interaction.SourceStart, scenes, beats, assets, true)
                : StoryPicture(interaction.AnchorOffset - 1, scenes, beats, assets, true))?.Url;

        // Option của FillBlank là đáp án chấp nhận nên không bao giờ trả.
        if (interaction.Type is ListeningInteractionType.RolePlay or ListeningInteractionType.FillBlank) return dto;
        if (interaction.Type == ListeningInteractionType.CharacterLetter)
        {
            var characterOption = own.FirstOrDefault(option => option.IsMatch);
            dto.CharacterImageUrl = characterOption is null ? null
                : ListeningMediaRequirements.FindImage(assets, characterOption.Id)?.Url;
            var units = VietnameseLetterUnits.Split(interaction.CharacterName!.Trim());
            dto.NameUnits = units.Select((unit, index) => index == interaction.BlankIndex ? null : unit).ToArray();
            dto.MaskedName = string.Concat(dto.NameUnits.Select(unit => unit ?? "_"));
            dto.LetterOptions = own.Select(option => new ReadingLetterOptionDto
            {
                Id = option.Id, Text = option.Text,
                SoundAudioUrl = ListeningMediaRequirements.FindAudio(assets, interaction.Id, option.Id, ListeningAudioParts.Sound)?.Url
            }).ToArray();
            return dto;
        }
        if (interaction.Type == ListeningInteractionType.WhoSaidIt)
        {
            dto.SpeakerLine = interaction.SourceQuote;
            dto.Speakers = own.Select(option => new ReadingSpeakerDto
            {
                Id = option.Id, Name = option.Text,
                ImageUrl = ListeningMediaRequirements.FindImage(assets, option.Id)?.Url,
                NameAudioUrl = ListeningMediaRequirements.FindAudio(assets, interaction.Id, option.Id, ListeningAudioParts.Text)?.Url
            }).ToArray();
            return dto;
        }
        var withFeedback = interaction.Type == ListeningInteractionType.PicturePrediction ||
            interaction.Type == ListeningInteractionType.Checkpoint && interaction.Kind != CheckpointKind.Literal;
        var cards = own.Select(option => new ReadingInteractionOptionDto
        {
            Id = option.Id,
            Text = option.Text,
            ImageUrl = interaction.Type == ListeningInteractionType.EventOrdering
                ? EventSceneImageUrl(option, scenes, beats, assets)
                : ListeningMediaRequirements.FindImage(assets, option.Id)?.Url,
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

    internal static List<ListeningInteractionOption> ShuffleItems(int sessionId, int interactionId,
        IReadOnlyList<ListeningInteractionOption> own, bool avoidCanonical = false)
    {
        var random = new Random(unchecked(sessionId * 7919 + interactionId));
        var shuffled = own.OrderBy(_ => random.Next()).ToList();
        if (avoidCanonical && shuffled.Count > 1 && shuffled.Select(x => x.Id)
            .SequenceEqual(own.OrderBy(x => x.CorrectOrder).Select(x => x.Id)))
            shuffled = shuffled.Skip(1).Append(shuffled[0]).ToList();
        return shuffled;
    }

    /// <summary>
    /// Tranh minh hoạ đã duyệt của đoạn chứa câu gốc của thẻ sự kiện, để bé chưa biết chữ xếp theo cảnh.
    /// Lấy tranh của beat chứa câu; scene chưa chia beat thì lấy tranh của scene. Không có tranh thì null (thẻ chỉ có chữ).
    /// </summary>
    internal static string? EventSceneImageUrl(ListeningInteractionOption option, IReadOnlyList<StoryScene> scenes,
        IReadOnlyList<IllustrationBeat> beats, IReadOnlyList<MediaAsset> assets)
    {
        return StoryPicture(option.SourceStart, scenes, beats, assets)?.Url;
    }

    internal static MediaAsset? StoryPicture(int? sourceStart, IReadOnlyList<StoryScene> scenes,
        IReadOnlyList<IllustrationBeat> beats, IReadOnlyList<MediaAsset> assets, bool requireValidated = false)
    {
        if (sourceStart is not int start) return null;
        var scene = scenes.FirstOrDefault(x => x.TextRangeStart <= start && start < x.TextRangeEnd);
        if (scene is null) return null;
        var local = start - scene.TextRangeStart;
        var sceneBeats = beats.Where(x => x.StorySceneId == scene.Id && !x.IsDeleted).OrderBy(x => x.BeatOrder).ToList();
        var beat = sceneBeats.FirstOrDefault(x => x.StartOffset <= local && local < x.EndOffset) ?? sceneBeats.FirstOrDefault();
        var illustrations = assets.Where(x => x.Type == MediaType.Illustration && x.Status == MediaStatus.Ready
            && !x.IsDeleted && !string.IsNullOrWhiteSpace(x.Url) &&
            (!requireValidated || x.ValidationStatus == ValidationStatus.Passed && x.IsCurrent)).ToList();
        var image = beat is null ? null : illustrations.Where(x => x.IllustrationBeatId == beat.Id)
            .OrderByDescending(x => x.IsCurrent).FirstOrDefault();
        return image ?? illustrations.FirstOrDefault(x => x.StorySceneId == scene.Id);
    }

    // Offset của segment tính trong SceneText; SourceStart tính trong cả truyện.
    internal static string? ModelAudioUrl(ListeningInteraction interaction, IReadOnlyList<StoryScene> scenes,
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
