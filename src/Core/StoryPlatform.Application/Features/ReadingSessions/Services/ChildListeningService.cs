using StoryPlatform.Application.Features.ParentAuthoring;
using System.Text;
using System.Text.Json;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ListeningInteractions.Services;
using StoryPlatform.Application.Features.ReadingSessions.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ReadingSessions.Services;

/// <summary>Trẻ trả lời tương tác lúc nghe (spec 8.3). Không chấm điểm, không đụng EXP, badge, quiz.</summary>
public class ChildListeningService : IChildListeningService
{
    public const string AnswerInvalidCode = "LISTENING_ANSWER_INVALID";
    public const string SessionClosedCode = "LISTENING_SESSION_CLOSED";
    public const int MaxAnswerLength = 100;

    private readonly IUnitOfWork _unitOfWork;
    private readonly IChildMediaUrlSigner? _mediaUrls;

    public ChildListeningService(IUnitOfWork unitOfWork, IChildMediaUrlSigner? mediaUrls = null)
    {
        _unitOfWork = unitOfWork;
        _mediaUrls = mediaUrls;
    }

    public async Task<ListeningAttemptResultDto> SubmitAttemptAsync(int childProfileId, int sessionId, int interactionId,
        SubmitListeningAttemptRequestDto request, CancellationToken ct = default)
    {
        var preview = await LoadAsync(childProfileId, sessionId, interactionId, ct);
        await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            // Cùng khóa theo truyện với ChildReadingService để hai lần bấm không ghi trùng.
            await _unitOfWork.AcquireTransactionLockAsync(preview.Session.StoryId, ct);
            var context = await LoadAsync(childProfileId, sessionId, interactionId, ct);
            var result = await AnswerAsync(context, request, ct);
            await _unitOfWork.SaveChangesAsync(ct);
            await _unitOfWork.CommitTransactionAsync(ct);
            return result;
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task SaveStepsAsync(int childProfileId, int sessionId, int interactionId,
        ListeningStepsRequestDto request, CancellationToken ct = default)
    {
        if (request.Sound is not ("tried" or "skipped") || request.Trace is not ("tried" or "skipped"))
            throw new BadRequestException(AnswerInvalidCode);
        var preview = await LoadAsync(childProfileId, sessionId, interactionId, ct);
        await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            await _unitOfWork.AcquireTransactionLockAsync(preview.Session.StoryId, ct);
            var context = await LoadAsync(childProfileId, sessionId, interactionId, ct);
            if (context.Interaction.Type != ListeningInteractionType.CharacterLetter)
                throw new NotFoundException("Tương tác", interactionId);
            var last = context.Attempts.LastOrDefault()
                       ?? throw new ConflictException("LISTENING_STEPS_BEFORE_ANSWER");
            last.StepResultsJson = JsonSerializer.Serialize(new { sound = request.Sound, trace = request.Trace });
            _unitOfWork.Repository<ListeningInteractionAttempt>().Update(last);
            await _unitOfWork.SaveChangesAsync(ct);
            await _unitOfWork.CommitTransactionAsync(ct);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            throw;
        }
    }

    private sealed record ListeningContext(ReadingSession Session, ListeningInteraction Interaction,
        IReadOnlyList<ListeningInteractionOption> Options, IReadOnlyList<StoryScene> Scenes,
        IReadOnlyList<MediaAsset> Assets, IReadOnlyList<ListeningInteractionAttempt> Attempts);

    private async Task<ListeningAttemptResultDto> AnswerAsync(ListeningContext context,
        SubmitListeningAttemptRequestDto request, CancellationToken ct)
    {
        var interaction = context.Interaction;
        // Loại khác Sắp xếp: mỗi phiên ghi một lần, gửi lại thì trả kết quả cũ (audio vẫn ký như lần đầu).
        var finished = interaction.Type == ListeningInteractionType.TrueFalse
            ? TrueFalseComplete(context)
            : interaction.Type == ListeningInteractionType.CauseEffect
            ? false   // mỗi lần gửi là một cặp; cặp đã xong được xử lý trong nhánh riêng bên dưới
            : ChildListeningContent.AllowsRetry(interaction)
            ? context.Attempts.Any(attempt => attempt.IsMatch == true) || context.Attempts.Count >= ChildListeningContent.MaxChoiceAttempts
            : interaction.Type == ListeningInteractionType.EventOrdering && interaction.TargetAge == 7
                ? context.Attempts.Any(attempt => attempt.IsMatch == true) ||
                    context.Attempts.Count(x => x.IsMatch == false) >= ChildListeningContent.RevealAfterWrongOrdersFor(interaction.TargetAge)
                : interaction.Type == ListeningInteractionType.EventOrdering ? false : context.Attempts.Count > 0;
        if (finished)
        {
            var previous = IsAge9Type(interaction.Type)
                ? Age9Result(context, context.Attempts[^1], context.Attempts)
                : IsAge8Type(interaction.Type)
                ? Age8Result(context, context.Attempts[^1], context.Attempts)
                : interaction.Type is ListeningInteractionType.PictureSearch or ListeningInteractionType.TrueFalse or
                ListeningInteractionType.FollowAction or ListeningInteractionType.SentenceBuilder
                ? Age7Result(context, context.Attempts[^1], context.Attempts)
                : interaction.Type == ListeningInteractionType.EventOrdering
                    ? OrderingResult(context, context.Attempts[^1], context.Attempts)
                    : Result(context, context.Attempts[^1], context.Attempts.Count);
            await SignAsync(previous, ct);
            return previous;
        }

        var attempt = new ListeningInteractionAttempt
        {
            ReadingSessionId = context.Session.Id,
            ChildProfileId = context.Session.ChildProfileId,
            ListeningInteractionId = interaction.Id,
            AttemptedAt = DateTime.UtcNow
        };
        switch (interaction.Type)
        {
            case ListeningInteractionType.Checkpoint or ListeningInteractionType.PicturePrediction
                or ListeningInteractionType.CharacterLetter or ListeningInteractionType.WhoSaidIt
                or ListeningInteractionType.InferenceQuestion or ListeningInteractionType.SceneTitle or ListeningInteractionType.ThoughtBubble:
            {
                // Manh mối của Suy luận không phải đáp án để chọn.
                var option = context.Options.FirstOrDefault(x => x.Id == request.OptionId && x.Role == ListeningOptionRole.None)
                             ?? throw new BadRequestException(AnswerInvalidCode);
                attempt.SelectedOptionId = option.Id;
                attempt.IsMatch = interaction.Type == ListeningInteractionType.PicturePrediction && interaction.TargetAge == 6
                    ? null : option.IsMatch;
                break;
            }
            case ListeningInteractionType.FillBlank:
            {
                var answer = request.Answer?.Trim();
                if (string.IsNullOrEmpty(answer) || answer.Length > MaxAnswerLength)
                    throw new BadRequestException(AnswerInvalidCode);
                attempt.AnswerText = answer;
                attempt.IsMatch = context.Options.Any(option => Normalize(option.Text) == Normalize(answer));
                break;
            }
            case ListeningInteractionType.EventOrdering:
            {
                var ordered = request.OrderedOptionIds ?? [];
                if (ordered.Count != context.Options.Count || ordered.Distinct().Count() != ordered.Count
                    || ordered.Any(id => context.Options.All(option => option.Id != id)))
                    throw new BadRequestException(AnswerInvalidCode);
                attempt.OrderedOptionIdsJson = JsonSerializer.Serialize(ordered);
                attempt.IsMatch = Misplaced(context.Options, ordered).Count == 0;
                break;
            }
            case ListeningInteractionType.PictureSearch:
            {
                var box = HotspotBox.TryParse(interaction.HotspotJson) ?? throw new BadRequestException(AnswerInvalidCode);
                if (request.X is not { } x || request.Y is not { } y || !double.IsFinite(x) || !double.IsFinite(y) ||
                    x is < 0 or > 1 || y is < 0 or > 1) throw new BadRequestException(AnswerInvalidCode);
                attempt.AnswerText = JsonSerializer.Serialize(new { x, y });
                attempt.IsMatch = box.Hits(x, y);
                break;
            }
            case ListeningInteractionType.TrueFalse:
            {
                var option = context.Options.FirstOrDefault(x => x.Id == request.OptionId)
                    ?? throw new BadRequestException(AnswerInvalidCode);
                if (request.Truth is not { } truth || option.Id != TrueFalseOpenOption(context)?.Id)
                    throw new BadRequestException(AnswerInvalidCode);
                attempt.SelectedOptionId = option.Id;
                attempt.AnswerText = truth ? "true" : "false";
                attempt.IsMatch = truth == option.IsMatch;
                break;
            }
            case ListeningInteractionType.FollowAction:
            {
                var item = context.Options.FirstOrDefault(x => x.Id == request.OptionId && x.Role == ListeningOptionRole.Item);
                var target = context.Options.FirstOrDefault(x => x.Id == request.TargetOptionId && x.Role == ListeningOptionRole.Target);
                if (item is null || target is null) throw new BadRequestException(AnswerInvalidCode);
                attempt.SelectedOptionId = item.Id;
                attempt.AnswerText = JsonSerializer.Serialize(new { target = target.Id });
                attempt.IsMatch = item.IsMatch;
                break;
            }
            case ListeningInteractionType.SentenceBuilder:
            {
                var ordered = request.OrderedOptionIds ?? [];
                if (ordered.Count != context.Options.Count || ordered.Distinct().Count() != ordered.Count ||
                    ordered.Any(id => context.Options.All(option => option.Id != id)))
                    throw new BadRequestException(AnswerInvalidCode);
                var chosen = ordered.Select(id => context.Options.First(option => option.Id == id).CorrectOrder ?? 0).ToArray();
                var accepted = SentenceOrders.Parse(interaction.AcceptedOrdersJson)
                    ?? throw new BadRequestException(AnswerInvalidCode);
                attempt.OrderedOptionIdsJson = JsonSerializer.Serialize(ordered);
                attempt.IsMatch = SentenceOrders.Matches(accepted, chosen);
                break;
            }
            case ListeningInteractionType.AlternateReading:
            {
                var speaker = context.Options.Select(x => x.Speaker).FirstOrDefault(name =>
                    !string.IsNullOrWhiteSpace(name) && string.Equals(name.Trim(), request.Speaker?.Trim(), StringComparison.OrdinalIgnoreCase))
                    ?? throw new BadRequestException(AnswerInvalidCode);
                attempt.AnswerText = speaker.Trim();
                break;
            }
            case ListeningInteractionType.CauseEffect:
            {
                var effect = context.Options.FirstOrDefault(x => x.Id == request.OptionId && x.Role == ListeningOptionRole.Item);
                var cause = context.Options.FirstOrDefault(x => x.Id == request.TargetOptionId && x.Role == ListeningOptionRole.Target);
                if (effect is null || cause is null) throw new BadRequestException(AnswerInvalidCode);
                var tries = context.Attempts.Where(x => x.SelectedOptionId == effect.Id).ToList();
                if (tries.Any(x => x.IsMatch == true) || tries.Count >= ChildListeningContent.MaxChoiceAttempts)
                {
                    // Thẻ này đã xong (nối đúng hoặc đã được nối sẵn): trả kết quả cũ, không ghi thêm dòng.
                    var done = Age8Result(context, tries[^1], context.Attempts);
                    await SignAsync(done, ct);
                    return done;
                }
                attempt.SelectedOptionId = effect.Id;
                attempt.AnswerText = JsonSerializer.Serialize(new { target = cause.Id });
                attempt.IsMatch = CausePairs.Matches(effect, cause);
                break;
            }
            case ListeningInteractionType.CharacterTrial:
            {
                if (request.Verdict is not ("reasonable" or "notYet" or "couldBeBetter"))
                    throw new BadRequestException(AnswerInvalidCode);
                await StoreFreeChoiceAsync(context, request, attempt, request.ReasonOptionId, ct);
                attempt.AnswerText = JsonSerializer.Serialize(new { verdict = request.Verdict,
                    freeText = request.FreeText?.Trim(), freeVoice = request.FreeVoice });
                break;
            }
            case ListeningInteractionType.WhatIf:
            {
                await StoreFreeChoiceAsync(context, request, attempt, request.OptionId, ct);
                attempt.AnswerText = JsonSerializer.Serialize(new { freeText = request.FreeText?.Trim(), freeVoice = request.FreeVoice });
                break;
            }
            case ListeningInteractionType.FindWrongDetail:
            {
                var sentences = SentenceSplitter.Split(interaction.Prompt ?? string.Empty);
                var option = context.Options.FirstOrDefault(value => value.Id == request.OptionId);
                if (request.SentenceIndex is not { } sentenceIndex || sentenceIndex < 0 ||
                    sentenceIndex >= sentences.Count || option is null)
                    throw new BadRequestException(AnswerInvalidCode);
                attempt.SelectedOptionId = option.Id;
                attempt.AnswerText = sentenceIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
                attempt.IsMatch = sentences[sentenceIndex].Contains(interaction.HiddenWord ?? string.Empty, StringComparison.Ordinal) &&
                    option.IsMatch;
                break;
            }
            case ListeningInteractionType.StoryEditor:
            {
                var kept = request.KeptOptionIds;
                if (kept is null || kept.Count == 0 || kept.Count != kept.Distinct().Count() ||
                    kept.Any(id => context.Options.All(option => option.Id != id)))
                    throw new BadRequestException(AnswerInvalidCode);
                var main = context.Options.Select(option => option.IsMatch).ToArray();
                var positions = kept.Select(id => Array.FindIndex(context.Options.ToArray(), option => option.Id == id)).ToHashSet();
                attempt.OrderedOptionIdsJson = JsonSerializer.Serialize(kept);
                attempt.IsMatch = EditorSentences.Grade(main, positions).Matched;
                break;
            }
            case ListeningInteractionType.RelationshipMap:
            {
                var map = RelationMap.TryParse(interaction.RelationsJson);
                var cards = context.Options.Where(option => option.Role == ListeningOptionRole.Card).ToArray();
                var placements = request.Placements;
                if (map is null || !map.IsValid(context.Options.Count(option => option.Role == ListeningOptionRole.Node), cards.Length) ||
                    placements is null || placements.Count != map.Edges.Count ||
                    placements.Select(value => value.EdgeIndex).Order().SequenceEqual(Enumerable.Range(0, map.Edges.Count)) == false ||
                    placements.Any(value => cards.All(card => card.Id != value.CardOptionId)))
                    throw new BadRequestException(AnswerInvalidCode);
                if (context.Attempts.Count > 0)
                {
                    var previous = JsonSerializer.Deserialize<List<PlacementDto>>(context.Attempts[^1].AnswerText ?? "[]") ?? [];
                    var previousIndexes = previous.ToDictionary(value => value.EdgeIndex,
                        value => Array.FindIndex(cards, card => card.Id == value.CardOptionId));
                    var previousGrade = map.Grade(previousIndexes);
                    if (placements.Any(value => previousGrade[value.EdgeIndex] &&
                        previous.First(old => old.EdgeIndex == value.EdgeIndex).CardOptionId != value.CardOptionId))
                        throw new BadRequestException(AnswerInvalidCode);
                }
                attempt.AnswerText = JsonSerializer.Serialize(placements);
                attempt.IsMatch = map.Grade(placements.ToDictionary(value => value.EdgeIndex,
                    value => Array.FindIndex(cards, card => card.Id == value.CardOptionId))).All(value => value);
                break;
            }
        }

        await _unitOfWork.Repository<ListeningInteractionAttempt>().AddAsync(attempt, ct);
        var result = IsAge9Type(interaction.Type)
            ? Age9Result(context, attempt, context.Attempts.Append(attempt).ToList())
            : IsAge8Type(interaction.Type)
            ? Age8Result(context, attempt, context.Attempts.Append(attempt).ToList())
            : interaction.Type == ListeningInteractionType.EventOrdering
            ? OrderingResult(context, attempt, context.Attempts.Append(attempt).ToList())
            : interaction.Type is ListeningInteractionType.PictureSearch or ListeningInteractionType.TrueFalse or
                ListeningInteractionType.FollowAction or ListeningInteractionType.SentenceBuilder
                ? Age7Result(context, attempt, context.Attempts.Append(attempt).ToList())
                : Result(context, attempt, context.Attempts.Count + 1);
        await SignAsync(result, ct);
        return result;
    }

    private async Task SignAsync(ListeningAttemptResultDto result, CancellationToken ct)
    {
        result.FeedbackAudioUrl = await _mediaUrls.SignAsync(result.FeedbackAudioUrl, ct);
        result.SentenceAudioUrl = await _mediaUrls.SignAsync(result.SentenceAudioUrl, ct);
        result.RevealImageUrl = await _mediaUrls.SignAsync(result.RevealImageUrl, ct);
        result.ExplanationAudioUrl = await _mediaUrls.SignAsync(result.ExplanationAudioUrl, ct);
        result.PositiveAudioUrl = await _mediaUrls.SignAsync(result.PositiveAudioUrl, ct);
        result.LimitationAudioUrl = await _mediaUrls.SignAsync(result.LimitationAudioUrl, ct);
        if (result.ModelSentenceAudioUrls is not null)
            result.ModelSentenceAudioUrls = (await Task.WhenAll(result.ModelSentenceAudioUrls
                .Select(url => _mediaUrls.SignAsync(url, ct)))).Select(url => url ?? string.Empty).ToList();
        if (result.ChainSentences is not null)
            foreach (var sentence in result.ChainSentences)
                sentence.AudioUrl = await _mediaUrls.SignAsync(sentence.AudioUrl, ct);
        if (result.Explanations is not null)
            result.Explanations = (await Task.WhenAll(result.Explanations.Select(async item =>
                item with { AudioUrl = await _mediaUrls.SignAsync(item.AudioUrl, ct) }))).ToList();
        if (result.EdgeResults is not null)
            result.EdgeResults = (await Task.WhenAll(result.EdgeResults.Select(async edge =>
                edge with { ExplanationAudioUrl = await _mediaUrls.SignAsync(edge.ExplanationAudioUrl, ct) }))).ToList();
    }

    private static bool IsAge8Type(ListeningInteractionType type) =>
        type is ListeningInteractionType.AlternateReading or ListeningInteractionType.InferenceQuestion or
            ListeningInteractionType.CauseEffect or ListeningInteractionType.SceneTitle or ListeningInteractionType.ThoughtBubble;

    private static bool IsAge9Type(ListeningInteractionType type) =>
        type is ListeningInteractionType.CharacterTrial or ListeningInteractionType.FindWrongDetail or
            ListeningInteractionType.RelationshipMap or ListeningInteractionType.StoryEditor or ListeningInteractionType.WhatIf;

    private async Task StoreFreeChoiceAsync(ListeningContext context, SubmitListeningAttemptRequestDto request,
        ListeningInteractionAttempt attempt, int? optionId, CancellationToken ct)
    {
        var free = request.FreeText?.Trim();
        var chosen = (optionId.HasValue ? 1 : 0) + (!string.IsNullOrWhiteSpace(free) ? 1 : 0) + (request.FreeVoice ? 1 : 0);
        if (chosen != 1 || free?.Length > MaxAnswerLength ||
            optionId is { } id && context.Options.All(option => option.Id != id))
            throw new BadRequestException(AnswerInvalidCode);
        if (request.FreeVoice && await _unitOfWork.Repository<ReadAloudRecording>().FirstOrDefaultAsync(
                row => row.ReadingSessionId == context.Session.Id &&
                    row.ListeningInteractionId == context.Interaction.Id && row.TotalCount == 0 && !row.IsDeleted,
                cancellationToken: ct) is null)
            throw new ConflictException("LISTENING_FREE_ANSWER_MISSING");
        attempt.SelectedOptionId = optionId;
    }

    private static ListeningAttemptResultDto Age9Result(ListeningContext context, ListeningInteractionAttempt attempt,
        IReadOnlyList<ListeningInteractionAttempt> attempts)
    {
        var interaction = context.Interaction;
        var result = new ListeningAttemptResultDto
            { InteractionId = interaction.Id, Type = interaction.Type.ToString(), OptionId = attempt.SelectedOptionId };
        if (interaction.Type is ListeningInteractionType.CharacterTrial or ListeningInteractionType.WhatIf)
        {
            result.Completed = true;
            var selected = context.Options.FirstOrDefault(option => option.Id == attempt.SelectedOptionId);
            if (selected is null)
            {
                result.Feedback = interaction.Type == ListeningInteractionType.CharacterTrial
                    ? "Cảm ơn con đã chia sẻ! Mỗi quyết định đều có mặt tốt và mặt cần cân nhắc."
                    : "Cảm ơn con đã nghĩ ra điều này! Mình cùng nghe tiếp nhé.";
                result.FeedbackAudioUrl = Audio(context, ListeningAudioParts.Thanks);
            }
            else if (interaction.Type == ListeningInteractionType.CharacterTrial)
            {
                result.Positive = selected.Feedback;
                result.Limitation = selected.Counterpoint;
                result.PositiveAudioUrl = ListeningMediaRequirements.FindAudio(context.Assets, interaction.Id,
                    selected.Id, ListeningAudioParts.Positive)?.Url;
                result.LimitationAudioUrl = ListeningMediaRequirements.FindAudio(context.Assets, interaction.Id,
                    selected.Id, ListeningAudioParts.Limitation)?.Url;
            }
            else
            {
                result.Feedback = selected.Feedback;
                result.FeedbackAudioUrl = ListeningMediaRequirements.FindAudio(context.Assets, interaction.Id,
                    selected.Id, ListeningAudioParts.Feedback)?.Url;
            }
            return result;
        }

        var matched = attempt.IsMatch == true;
        var revealed = !matched && attempts.Count >= ChildListeningContent.MaxChoiceAttempts;
        result.Matched = matched;
        result.CanRetry = !matched && !revealed;
        result.Revealed = revealed;
        result.Completed = matched || revealed;
        if (matched)
        {
            result.Feedback = interaction.MatchFeedback;
            result.FeedbackAudioUrl = Audio(context, ListeningAudioParts.Match);
        }
        else if (!revealed)
        {
            var key = interaction.Type == ListeningInteractionType.RelationshipMap
                ? ListeningAudioParts.RetryTry : interaction.Type == ListeningInteractionType.StoryEditor
                    ? ListeningAudioParts.RetryHear : ListeningAudioParts.RetryLookClose;
            result.Feedback = ListeningAudioParts.FixedLine(key);
            result.FeedbackAudioUrl = Audio(context, key);
        }

        if (interaction.Type == ListeningInteractionType.FindWrongDetail)
        {
            var sentences = SentenceSplitter.Split(interaction.Prompt ?? string.Empty);
            var chosen = int.TryParse(attempt.AnswerText, out var index) ? index : -1;
            var correctIndex = Enumerable.Range(0, sentences.Count).FirstOrDefault(i =>
                sentences[i].Contains(interaction.HiddenWord ?? string.Empty, StringComparison.Ordinal), -1);
            result.SentenceCorrect = chosen == correctIndex;
            if (revealed)
            {
                result.CorrectSentenceIndex = correctIndex;
                result.CorrectOptionId = context.Options.FirstOrDefault(option => option.IsMatch)?.Id;
            }
            if (matched || revealed)
            {
                result.Explanation = interaction.Explanation;
                result.ExplanationAudioUrl = Audio(context, ListeningAudioParts.Explanation);
                if (interaction.SourceStart is { } start && interaction.SourceEnd is { } end)
                    result.SourceSpan = new ClueSpanDto { Start = start, End = end };
            }
        }
        else if (interaction.Type == ListeningInteractionType.StoryEditor)
        {
            var kept = JsonSerializer.Deserialize<int[]>(attempt.OrderedOptionIdsJson ?? "[]") ?? [];
            var grade = EditorSentences.Grade(context.Options.Select(option => option.IsMatch).ToArray(),
                kept.Select(id => Array.FindIndex(context.Options.ToArray(), option => option.Id == id)).ToHashSet());
            result.TooLong = grade.TooLong;
            if (revealed) result.MissingMainOptionIds = grade.MissingMain.Select(index => context.Options[index].Id).ToList();
            if (matched || revealed)
            {
                result.ModelKeptOptionIds = EditorSentences.Model(context.Options.Select(option => option.IsMatch).ToArray())
                    .Select(index => context.Options[index].Id).ToList();
                result.ModelSentenceAudioUrls = result.ModelKeptOptionIds.Select(id =>
                    ListeningMediaRequirements.FindAudio(context.Assets, interaction.Id, id,
                        ListeningAudioParts.Text)?.Url ?? string.Empty).ToList();
                result.Explanations = context.Options.Where(option => option.IsMatch).Select(option =>
                    new EditorExplanationDto(option.Id, option.Feedback ?? string.Empty,
                        ListeningMediaRequirements.FindAudio(context.Assets, interaction.Id, option.Id,
                            ListeningAudioParts.Feedback)?.Url)).ToList();
            }
        }
        else if (interaction.Type == ListeningInteractionType.RelationshipMap)
        {
            var map = RelationMap.TryParse(interaction.RelationsJson)!;
            var cards = context.Options.Where(option => option.Role == ListeningOptionRole.Card).ToArray();
            var placements = JsonSerializer.Deserialize<List<PlacementDto>>(attempt.AnswerText ?? "[]") ?? [];
            var grades = map.Grade(placements.ToDictionary(value => value.EdgeIndex,
                value => Array.FindIndex(cards, card => card.Id == value.CardOptionId)));
            result.EdgeResults = map.Edges.Select((edge, edgeIndex) =>
            {
                var show = grades[edgeIndex] || revealed;
                return new EdgeResultDto(edgeIndex, grades[edgeIndex], show ? edge.Explanation : null,
                    show ? Audio(context, ListeningAudioParts.RelationKey(edgeIndex)) : null,
                    revealed ? cards[edge.Accepted[0]].Id : null);
            }).ToList();
        }
        return result;
    }

    // Bộ tuổi 8: không có chữ "sai" và không có điểm; lộ đáp án (kèm giải thích) khi hết lượt chọn.
    private static ListeningAttemptResultDto Age8Result(ListeningContext context, ListeningInteractionAttempt attempt,
        IReadOnlyList<ListeningInteractionAttempt> attempts)
    {
        var interaction = context.Interaction;
        var result = new ListeningAttemptResultDto { InteractionId = interaction.Id, Type = interaction.Type.ToString() };
        if (interaction.Type == ListeningInteractionType.AlternateReading)
        {
            result.Speaker = attempt.AnswerText;
            result.Completed = true;
            return result;
        }
        if (interaction.Type == ListeningInteractionType.CauseEffect) return CauseEffectResult(context, attempt, attempts, result);

        var answers = context.Options.Where(x => x.Role == ListeningOptionRole.None).ToList();
        var selected = answers.FirstOrDefault(x => x.Id == attempt.SelectedOptionId);
        var correct = answers.First(x => x.IsMatch);
        var matched = attempt.IsMatch == true;
        var revealed = !matched && attempts.Count >= ChildListeningContent.MaxChoiceAttempts;
        result.OptionId = attempt.SelectedOptionId;
        result.Matched = matched;
        result.Revealed = revealed;
        result.CanRetry = !matched && !revealed;
        result.Completed = matched || revealed;
        if (revealed) result.CorrectOptionId = correct.Id;

        if (interaction.Type == ListeningInteractionType.InferenceQuestion && interaction.SourceQuote is { } passage &&
            ClueSpans.Find(passage, context.Options.Where(x => x.Role == ListeningOptionRole.Clue).Select(x => x.Text).ToArray()) is { } spans)
            // Manh mối lộ khi đúng, khi chọn lại và khi lộ đáp án.
            result.ClueSpans = spans.Select(span => new ClueSpanDto { Start = span.Start, End = span.End }).ToList();

        if (matched)
        {
            result.Feedback = interaction.MatchFeedback;
            result.FeedbackAudioUrl = Audio(context, ListeningAudioParts.Match);
        }
        else if (!revealed)
        {
            // Chọn lại: Đặt tên cảnh nói vì sao tiêu đề đã chọn chỉ là một chi tiết; hai loại kia dùng câu cố định.
            var retryKey = interaction.Type == ListeningInteractionType.ThoughtBubble ? ListeningAudioParts.RetryLook : ListeningAudioParts.RetryLookClose;
            if (interaction.Type == ListeningInteractionType.SceneTitle)
            {
                result.Feedback = selected?.Feedback;
                result.FeedbackAudioUrl = selected is null ? null : ListeningMediaRequirements.FindAudio(
                    context.Assets, interaction.Id, selected.Id, ListeningAudioParts.Feedback)?.Url;
            }
            else
            {
                result.Feedback = ListeningAudioParts.FixedLine(retryKey);
                result.FeedbackAudioUrl = Audio(context, retryKey);
            }
        }
        else if (interaction.Type == ListeningInteractionType.SceneTitle)
        {
            result.Feedback = correct.Feedback;
            result.FeedbackAudioUrl = ListeningMediaRequirements.FindAudio(context.Assets, interaction.Id, correct.Id, ListeningAudioParts.Feedback)?.Url;
        }

        if (matched || revealed)
        {
            if (interaction.Type is ListeningInteractionType.InferenceQuestion or ListeningInteractionType.SceneTitle)
            {
                result.Explanation = interaction.Explanation;
                result.ExplanationAudioUrl = Audio(context, ListeningAudioParts.Explanation);
            }
            if (interaction.Type == ListeningInteractionType.ThoughtBubble) result.RevealEmotion = correct.Emotion;
        }
        return result;
    }

    private static ListeningAttemptResultDto CauseEffectResult(ListeningContext context, ListeningInteractionAttempt attempt,
        IReadOnlyList<ListeningInteractionAttempt> attempts, ListeningAttemptResultDto result)
    {
        var interaction = context.Interaction;
        var effects = context.Options.Where(x => x.Role == ListeningOptionRole.Item).OrderBy(x => x.CorrectOrder).ToList();
        var effect = effects.FirstOrDefault(x => x.Id == attempt.SelectedOptionId);
        var tries = attempts.Where(x => x.SelectedOptionId == attempt.SelectedOptionId).ToList();
        var matched = attempt.IsMatch == true;
        var revealed = !matched && tries.Count >= ChildListeningContent.MaxChoiceAttempts;
        result.OptionId = attempt.SelectedOptionId;
        result.Matched = matched;
        result.Revealed = revealed;
        result.CanRetry = !matched && !revealed;
        if (effect is not null && (matched || revealed))
            result.MatchedCauseOptionId = context.Options.FirstOrDefault(x => CausePairs.Matches(effect, x))?.Id;
        if (result.CanRetry)
        {
            result.Feedback = ListeningAudioParts.FixedLine(ListeningAudioParts.RetryTry);
            result.FeedbackAudioUrl = Audio(context, ListeningAudioParts.RetryTry);
        }

        bool Done(ListeningInteractionOption item)
        {
            var mine = attempts.Where(x => x.SelectedOptionId == item.Id).ToList();
            return mine.Any(x => x.IsMatch == true) || mine.Count >= ChildListeningContent.MaxChoiceAttempts;
        }
        result.Completed = effects.Count > 0 && effects.All(Done);
        if (result.Completed)
        {
            result.Feedback = interaction.MatchFeedback;
            result.FeedbackAudioUrl = Audio(context, ListeningAudioParts.Match);
            result.ChainSentences = effects.Select(item =>
            {
                var cause = context.Options.FirstOrDefault(x => CausePairs.Matches(item, x));
                return new ChainSentenceDto
                {
                    EffectOptionId = item.Id,
                    Text = cause is null ? item.Text : CausePairs.Sentence(cause.Text, item.Text),
                    AudioUrl = ListeningMediaRequirements.FindAudio(context.Assets, interaction.Id, item.Id, ListeningAudioParts.Chain)?.Url
                };
            }).ToList();
        }
        return result;
    }

    private static ListeningAttemptResultDto Result(ListeningContext context, ListeningInteractionAttempt attempt, int attemptsSoFar)
    {
        var interaction = context.Interaction;
        var result = new ListeningAttemptResultDto { InteractionId = interaction.Id, Type = interaction.Type.ToString() };
        if (ChildListeningContent.AllowsRetry(interaction))
        {
            var selected = context.Options.FirstOrDefault(option => option.Id == attempt.SelectedOptionId);
            var correct = context.Options.First(option => option.IsMatch);
            var matched = attempt.IsMatch == true;
            var revealed = !matched && attemptsSoFar >= ChildListeningContent.MaxChoiceAttempts;
            result.Matched = matched;
            result.CanRetry = !matched && !revealed;
            result.Revealed = revealed;
            result.CorrectOptionId = revealed ? correct.Id : null;
            if (matched)
            {
                result.Feedback = interaction.Type == ListeningInteractionType.Checkpoint ? selected?.Feedback : interaction.MatchFeedback;
                result.FeedbackAudioUrl = interaction.Type == ListeningInteractionType.Checkpoint
                    ? ListeningMediaRequirements.FindAudio(context.Assets, interaction.Id, selected?.Id, ListeningAudioParts.Feedback)?.Url
                    : Audio(context, ListeningAudioParts.Match);
            }
            else if (revealed)
            {
                result.Feedback = interaction.Type switch
                {
                    ListeningInteractionType.Checkpoint => correct.Feedback,
                    ListeningInteractionType.WhoSaidIt => $"Bạn {correct.Text} đã nói câu này đó.",
                    _ => $"Chữ còn thiếu là {correct.Text}."
                };
                result.FeedbackAudioUrl = interaction.Type == ListeningInteractionType.Checkpoint
                    ? ListeningMediaRequirements.FindAudio(context.Assets, interaction.Id, correct.Id, ListeningAudioParts.Feedback)?.Url
                    : ListeningMediaRequirements.FindAudio(context.Assets, interaction.Id, correct.Id,
                        interaction.Type == ListeningInteractionType.CharacterLetter ? ListeningAudioParts.Sound : ListeningAudioParts.Text)?.Url;
            }
            else
            {
                result.Feedback = interaction.Type switch
                {
                    ListeningInteractionType.Checkpoint => ListeningAudioParts.FixedLine(ListeningAudioParts.RetryLook),
                    ListeningInteractionType.WhoSaidIt => interaction.NoMatchFeedback,
                    _ => "Mình chọn lại nhé."
                };
                result.FeedbackAudioUrl = interaction.Type switch
                {
                    ListeningInteractionType.Checkpoint => Audio(context, ListeningAudioParts.RetryLook),
                    ListeningInteractionType.WhoSaidIt => Audio(context, ListeningAudioParts.NoMatch),
                    _ => null
                };
            }
            return result;
        }
        switch (interaction.Type)
        {
            case ListeningInteractionType.Checkpoint or ListeningInteractionType.PicturePrediction:
            {
                var option = context.Options.FirstOrDefault(x => x.Id == attempt.SelectedOptionId);
                result.Feedback = option?.Feedback;
                result.FeedbackAudioUrl = option is null ? null : ListeningMediaRequirements.FindAudio(
                    context.Assets, interaction.Id, option.Id, ListeningAudioParts.Feedback)?.Url;
                break;
            }
            case ListeningInteractionType.RolePlay:
                result.Feedback = interaction.MatchFeedback;
                result.FeedbackAudioUrl = Audio(context, ListeningAudioParts.Match);
                break;
            case ListeningInteractionType.FillBlank:
            {
                var matched = attempt.IsMatch == true;
                result.Matched = matched;
                result.Feedback = matched ? interaction.MatchFeedback : interaction.NoMatchFeedback;
                result.FeedbackAudioUrl = Audio(context, matched ? ListeningAudioParts.Match : ListeningAudioParts.NoMatch);
                result.SuggestedAnswer = matched ? null : interaction.HiddenWord;
                break;
            }
        }

        return result;
    }

    private static ListeningInteractionOption? TrueFalseOpenOption(ListeningContext context) =>
        ChildListeningContent.ShuffleItems(context.Session.Id, context.Interaction.Id, context.Options)
            .FirstOrDefault(option =>
        {
            var attempts = context.Attempts.Where(x => x.SelectedOptionId == option.Id).ToArray();
            return !attempts.Any(x => x.IsMatch == true) && attempts.Length < ChildListeningContent.MaxChoiceAttempts;
        });

    private static bool TrueFalseComplete(ListeningContext context) => TrueFalseOpenOption(context) is null;

    private static ListeningAttemptResultDto Age7Result(ListeningContext context, ListeningInteractionAttempt attempt,
        IReadOnlyList<ListeningInteractionAttempt> attempts)
    {
        var interaction = context.Interaction;
        var matched = attempt.IsMatch == true;
        var count = interaction.Type == ListeningInteractionType.TrueFalse
            ? attempts.Count(x => x.SelectedOptionId == attempt.SelectedOptionId)
            : attempts.Count;
        var revealed = !matched && count >= ChildListeningContent.MaxChoiceAttempts;
        var result = new ListeningAttemptResultDto
        {
            InteractionId = interaction.Id, Type = interaction.Type.ToString(), OptionId = attempt.SelectedOptionId,
            Matched = matched, Revealed = revealed, CanRetry = !matched && !revealed,
            Completed = interaction.Type == ListeningInteractionType.TrueFalse
                ? context.Options.All(option =>
                {
                    var mine = attempts.Where(x => x.SelectedOptionId == option.Id).ToArray();
                    return mine.Any(x => x.IsMatch == true) || mine.Length >= ChildListeningContent.MaxChoiceAttempts;
                })
                : interaction.Type != ListeningInteractionType.SentenceBuilder && (matched || revealed) ||
                    interaction.Type == ListeningInteractionType.SentenceBuilder && revealed
        };
        if (matched)
        {
            result.Feedback = interaction.MatchFeedback;
            result.FeedbackAudioUrl = Audio(context, ListeningAudioParts.Match);
        }
        else if (revealed)
        {
            result.Feedback = interaction.NoMatchFeedback;
            result.FeedbackAudioUrl = Audio(context, ListeningAudioParts.NoMatch);
            if (interaction.Type == ListeningInteractionType.TrueFalse)
                RevealStatement(context, attempt, result);
            if (interaction.Type == ListeningInteractionType.PictureSearch && HotspotBox.TryParse(interaction.HotspotJson) is { } box)
                result.RevealHotspot = new HotspotDto { X = box.X, Y = box.Y, W = box.W, H = box.H };
            if (interaction.Type == ListeningInteractionType.FollowAction)
                result.CorrectOptionId = context.Options.FirstOrDefault(x => x.Role == ListeningOptionRole.Item && x.IsMatch)?.Id;
            if (interaction.Type == ListeningInteractionType.SentenceBuilder)
                result.CorrectOrderOptionIds = context.Options.OrderBy(x => x.CorrectOrder).Select(x => x.Id).ToList();
        }
        else
        {
            var retryKey = interaction.Type switch
            {
                ListeningInteractionType.PictureSearch => ListeningAudioParts.RetryLookClose,
                ListeningInteractionType.SentenceBuilder => ListeningAudioParts.RetryHear,
                _ => ListeningAudioParts.RetryTry
            };
            result.Feedback = interaction.Type == ListeningInteractionType.TrueFalse
                ? interaction.NoMatchFeedback : ListeningAudioParts.FixedLine(retryKey);
            result.FeedbackAudioUrl = Audio(context, interaction.Type == ListeningInteractionType.TrueFalse
                ? ListeningAudioParts.NoMatch : retryKey);
        }
        if (interaction.Type == ListeningInteractionType.SentenceBuilder && (matched || revealed))
        {
            var orderIndex = 0;
            if (matched && attempt.OrderedOptionIdsJson is { } chosenJson &&
                SentenceOrders.Parse(interaction.AcceptedOrdersJson) is { } accepted)
            {
                var ids = JsonSerializer.Deserialize<int[]>(chosenJson) ?? [];
                var order = ids.Select(id => context.Options.First(x => x.Id == id).CorrectOrder ?? 0).ToArray();
                var found = Enumerable.Range(0, accepted.Count).FirstOrDefault(index => accepted[index].SequenceEqual(order));
                orderIndex = found;
            }
            var sentenceAudio = ListeningMediaRequirements.FindAudio(context.Assets, interaction.Id, null,
                ListeningAudioParts.SentenceKey(orderIndex));
            result.SentenceAudioUrl = sentenceAudio?.Url;
            result.SentenceWordTimings = sentenceAudio?.WordTimings;
        }
        return result;
    }

    // Đúng/sai sai lần 2: nói đáp án của câu và, nếu đã định vị được chi tiết bằng chứng, chỉ vào tranh.
    private static void RevealStatement(ListeningContext context, ListeningInteractionAttempt attempt, ListeningAttemptResultDto result)
    {
        var option = context.Options.FirstOrDefault(x => x.Id == attempt.SelectedOptionId);
        if (option is null) return;
        result.CorrectTruth = option.IsMatch;
        var key = option.IsMatch ? ListeningAudioParts.TruthYes : ListeningAudioParts.TruthNo;
        result.Feedback = ListeningAudioParts.FixedLine(key);
        result.FeedbackAudioUrl = Audio(context, key);
        // Câu đúng: chỉ vào chi tiết chứng minh. Câu không đúng: chỉ vào chi tiết thật mà câu đó nói lệch.
        if (HotspotBox.TryParse(option.HotspotJson) is not { } box) return;
        var picture = context.Assets.FirstOrDefault(x => x.Id == box.AssetId && x.Type == MediaType.Illustration &&
            ListeningMediaRequirements.IsUsable(x));
        if (picture is null) return;
        result.RevealHotspot = new HotspotDto { X = box.X, Y = box.Y, W = box.W, H = box.H };
        result.RevealImageUrl = picture.Url;
    }

    private static ListeningAttemptResultDto OrderingResult(ListeningContext context,
        ListeningInteractionAttempt attempt, IReadOnlyList<ListeningInteractionAttempt> sessionAttempts)
    {
        var interaction = context.Interaction;
        var matched = attempt.IsMatch == true;
        var revealed = sessionAttempts.Count(x => x.IsMatch == false) >= ChildListeningContent.RevealAfterWrongOrdersFor(interaction.TargetAge);
        var result = new ListeningAttemptResultDto
        {
            InteractionId = interaction.Id,
            Type = interaction.Type.ToString(),
            Matched = matched,
            Feedback = matched ? interaction.MatchFeedback : interaction.NoMatchFeedback,
            FeedbackAudioUrl = Audio(context, matched ? ListeningAudioParts.Match : ListeningAudioParts.NoMatch),
            Revealed = revealed
        };
        if (revealed)
        {
            result.CorrectOrderOptionIds = context.Options.OrderBy(x => x.CorrectOrder).Select(x => x.Id).ToList();
        }
        else if (!matched)
        {
            var ordered = JsonSerializer.Deserialize<List<int>>(attempt.OrderedOptionIdsJson!)!;
            result.ReplaySceneIndexes = Misplaced(context.Options, ordered)
                .Select(option => SceneIndexOf(context.Scenes, option.SourceStart))
                .OfType<int>().Distinct().Order().ToList();
        }

        return result;
    }

    // Thẻ ở vị trí i (tính từ 1) đặt sai khi CorrectOrder khác i.
    private static List<ListeningInteractionOption> Misplaced(IReadOnlyList<ListeningInteractionOption> options,
        IReadOnlyList<int> ordered) =>
        ordered.Select((id, index) => (Option: options.First(option => option.Id == id), Position: index + 1))
            .Where(x => x.Option.CorrectOrder != x.Position).Select(x => x.Option).ToList();

    private static int? SceneIndexOf(IReadOnlyList<StoryScene> scenes, int? offset) =>
        offset is int value ? scenes.FirstOrDefault(scene => scene.TextRangeStart <= value && value < scene.TextRangeEnd)?.SceneIndex : null;

    private static string? Audio(ListeningContext context, string partKey) =>
        ListeningMediaRequirements.FindAudio(context.Assets, context.Interaction.Id, null, partKey)?.Url;

    // Không phân biệt hoa thường, bỏ khoảng trắng thừa, chuẩn hoá dấu tiếng Việt.
    private static string Normalize(string value) =>
        string.Join(' ', value.Normalize(NormalizationForm.FormC).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .ToLowerInvariant();

    /// <summary>Kiểm phiên và tương tác như GET content: chỉ tương tác đã gắn scene và đủ media.</summary>
    private async Task<ListeningContext> LoadAsync(int childProfileId, int sessionId, int interactionId, CancellationToken ct)
    {
        // Read without tracking so revalidation after the workflow lock sees committed interruptions.
        var session = await _unitOfWork.Repository<ReadingSession>().FirstOrDefaultAsync(
            value => value.Id == sessionId, cancellationToken: ct);
        if (session == null || session.IsDeleted || session.ChildProfileId != childProfileId)
            throw new NotFoundException("Phiên đọc", sessionId);
        if (session.ForceExitRequestedAt != null || session.Status == SessionStatus.ForceExited)
            throw new ForbiddenException(ReadingSessionInterruptPolicy.ChildMessage);
        // Tương tác hiện trong lúc nghe, trước khi gọi complete và trước Quiz.
        if (!ChildReadingPolicy.IsActive(session.Status))
            throw new ConflictException(SessionClosedCode);

        var parentPackage = await ParentStoryPackageReader.ForSessionAsync(_unitOfWork, session, ct);
        var profile = await _unitOfWork.Repository<ChildProfile>().GetByIdAsync(childProfileId, ct);
        if (profile is null || profile.IsDeleted) throw new NotFoundException("Hồ sơ trẻ", childProfileId);
        var childAge = ListeningAgeSets.AgeFor(profile.Age, profile.AgeBand);
        var versionId = session.StoryVersionId ?? throw new ForbiddenException(ChildReadingPolicy.StoryUnavailableMessage);
        var interaction = await _unitOfWork.Repository<ListeningInteraction>().FirstOrDefaultAsync(
            value => value.Id == interactionId && value.StoryVersionId == versionId && value.ParentStoryPackageId == session.ParentStoryPackageId && !value.IsDeleted, cancellationToken: ct);
        if (parentPackage is not null && !System.Text.Json.JsonSerializer.Deserialize<ParentPackageManifest>(parentPackage.ManifestJson, ParentPackageContext.Json)!.ListeningIds.Contains(interactionId))
            throw new NotFoundException("Interaction", interactionId);
        if (interaction?.StorySceneId is null || interaction.TargetAge != childAge) throw new NotFoundException("Tương tác", interactionId);

        var options = (await _unitOfWork.Repository<ListeningInteractionOption>().FindAsync(
                value => value.ListeningInteractionId == interactionId && !value.IsDeleted, cancellationToken: ct))
            .OrderBy(value => value.SortOrder).ToList();
        var assets = await _unitOfWork.Repository<MediaAsset>().FindAsync(
            value => value.StoryVersionId == versionId &&
                (value.ListeningInteractionId == interactionId || value.Type == MediaType.Illustration) && !value.IsDeleted,
            cancellationToken: ct);
        if (parentPackage is not null)
        {
            var manifest = System.Text.Json.JsonSerializer.Deserialize<ParentPackageManifest>(parentPackage.ManifestJson, ParentPackageContext.Json)!;
            assets = assets.Where(x => manifest.MediaAssetIds.Contains(x.Id)).Select(x => ParentStoryPackageReader.ProxyAsset(x, session.FamilyConfirmationId!.Value)).ToArray();
        }
        if (ListeningMediaRequirements.Missing(interaction, options, assets).Count > 0)
            throw new NotFoundException("Tương tác", interactionId);

        var scenes = await _unitOfWork.Repository<StoryScene>().FindAsync(
            value => value.StoryVersionId == versionId && !value.IsDeleted, cancellationToken: ct);
        var attempts = (await _unitOfWork.Repository<ListeningInteractionAttempt>().FindAsync(
                value => value.ReadingSessionId == sessionId && value.ListeningInteractionId == interactionId && !value.IsDeleted,
                cancellationToken: ct))
            .OrderBy(value => value.Id).ToList();
        return new ListeningContext(session, interaction, options, scenes, assets, attempts);
    }
}
