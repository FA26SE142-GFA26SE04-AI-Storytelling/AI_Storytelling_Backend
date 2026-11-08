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

    private sealed record ListeningContext(ReadingSession Session, ListeningInteraction Interaction,
        IReadOnlyList<ListeningInteractionOption> Options, IReadOnlyList<StoryScene> Scenes,
        IReadOnlyList<MediaAsset> Assets, IReadOnlyList<ListeningInteractionAttempt> Attempts);

    private async Task<ListeningAttemptResultDto> AnswerAsync(ListeningContext context,
        SubmitListeningAttemptRequestDto request, CancellationToken ct)
    {
        var interaction = context.Interaction;
        // Loại khác Sắp xếp: mỗi phiên ghi một lần, gửi lại thì trả kết quả cũ.
        if (interaction.Type != ListeningInteractionType.EventOrdering && context.Attempts.Count > 0)
            return Result(context, context.Attempts[0]);

        var attempt = new ListeningInteractionAttempt
        {
            ReadingSessionId = context.Session.Id,
            ChildProfileId = context.Session.ChildProfileId,
            ListeningInteractionId = interaction.Id,
            AttemptedAt = DateTime.UtcNow
        };
        switch (interaction.Type)
        {
            case ListeningInteractionType.Checkpoint or ListeningInteractionType.PicturePrediction:
            {
                var option = context.Options.FirstOrDefault(x => x.Id == request.OptionId)
                             ?? throw new BadRequestException(AnswerInvalidCode);
                attempt.SelectedOptionId = option.Id;
                attempt.IsMatch = option.IsMatch;
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
        }

        await _unitOfWork.Repository<ListeningInteractionAttempt>().AddAsync(attempt, ct);
        var result = interaction.Type == ListeningInteractionType.EventOrdering
            ? OrderingResult(context, attempt, context.Attempts.Append(attempt).ToList())
            : Result(context, attempt);
        result.FeedbackAudioUrl = await _mediaUrls.SignAsync(result.FeedbackAudioUrl, ct);
        return result;
    }

    private static ListeningAttemptResultDto Result(ListeningContext context, ListeningInteractionAttempt attempt)
    {
        var interaction = context.Interaction;
        var result = new ListeningAttemptResultDto { InteractionId = interaction.Id, Type = interaction.Type.ToString() };
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

    private static ListeningAttemptResultDto OrderingResult(ListeningContext context,
        ListeningInteractionAttempt attempt, IReadOnlyList<ListeningInteractionAttempt> sessionAttempts)
    {
        var interaction = context.Interaction;
        var matched = attempt.IsMatch == true;
        var revealed = sessionAttempts.Count(x => x.IsMatch == false) >= ChildListeningContent.RevealAfterWrongOrders;
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
        var versionId = session.StoryVersionId ?? throw new ForbiddenException(ChildReadingPolicy.StoryUnavailableMessage);
        var interaction = await _unitOfWork.Repository<ListeningInteraction>().FirstOrDefaultAsync(
            value => value.Id == interactionId && value.StoryVersionId == versionId && value.ParentStoryPackageId == session.ParentStoryPackageId && !value.IsDeleted, cancellationToken: ct);
        if (parentPackage is not null && !System.Text.Json.JsonSerializer.Deserialize<ParentPackageManifest>(parentPackage.ManifestJson, ParentPackageContext.Json)!.ListeningIds.Contains(interactionId))
            throw new NotFoundException("Interaction", interactionId);
        if (interaction?.StorySceneId is null) throw new NotFoundException("Tương tác", interactionId);

        var options = (await _unitOfWork.Repository<ListeningInteractionOption>().FindAsync(
                value => value.ListeningInteractionId == interactionId && !value.IsDeleted, cancellationToken: ct))
            .OrderBy(value => value.SortOrder).ToList();
        var assets = await _unitOfWork.Repository<MediaAsset>().FindAsync(
            value => value.StoryVersionId == versionId && value.ListeningInteractionId == interactionId && !value.IsDeleted,
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
