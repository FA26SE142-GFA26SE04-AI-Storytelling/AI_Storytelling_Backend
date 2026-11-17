using StoryPlatform.Application.Features.ParentAuthoring;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.MediaStorage.Interfaces;
using StoryPlatform.Application.Features.Notifications.Interfaces;
using StoryPlatform.Application.Features.ReadingSessions.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ReadingSessions.Services;

/// <summary>Trẻ gửi ghi âm kể lại hoặc câu hỏi về tranh/cảnh cho chủ hồ sơ. Không dùng AI.</summary>
public class ChildStorySummaryService : IChildStorySummaryService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMediaStorage _storage;
    private readonly INotificationService _notifications;
    private readonly ILogger<ChildStorySummaryService> _logger;

    public ChildStorySummaryService(IUnitOfWork unitOfWork, IMediaStorage storage,
        INotificationService notifications, ILogger<ChildStorySummaryService> logger)
    {
        _unitOfWork = unitOfWork;
        _storage = storage;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task<StorySummarySentDto> SendRecordingAsync(
        int childProfileId, int sessionId, StorySummaryRecordingRequest request, CancellationToken ct = default)
    {
        var session = await LoadSessionAsync(childProfileId, sessionId, ct);
        await EnsureAudioAsync(childProfileId, request.Audio, request.ContentType, ct);

        var summary = await FindOrNewAsync(session, ct);
        if (summary.AudioStoragePath != null)
            throw new ConflictException(StorySummaryPolicy.AlreadyRecordedMessage);

        var contentType = request.ContentType!.Split(';')[0].Trim().ToLowerInvariant();
        var path = $"story-summaries/{childProfileId}/{sessionId}/{Guid.NewGuid():N}{StorySummaryPolicy.Extension(contentType)}";
        using (var content = new MemoryStream(request.Audio))
        {
            await _storage.UploadAsync(path, content, contentType, ct);
        }

        summary.AudioStoragePath = path;
        summary.AudioContentType = contentType;
        summary.AudioBytes = request.Audio.Length;
        summary.RecordingSentAt = DateTime.UtcNow;
        try
        {
            await SaveSummaryAsync(summary, ct);
        }
        catch
        {
            await TryDeleteAsync(path);
            throw;
        }

        await NotifyOwnerAsync(session, summary, "recording", ct);
        return Sent(summary);
    }

    public async Task<StorySummaryQuestionSentDto> AskAsync(
        int childProfileId, int sessionId, StorySummaryQuestionRequest request, CancellationToken ct = default)
    {
        var session = await LoadSessionAsync(childProfileId, sessionId, ct);
        var text = StorySummaryPolicy.NormalizeQuestionText(request.Text);
        if (text is { Length: > StorySummaryPolicy.MaxQuestionTextLength })
            throw new BadRequestException(StorySummaryPolicy.QuestionTooLongMessage);
        var hasAudio = request.Audio.Length > 0;
        if (hasAudio)
            await EnsureAudioAsync(childProfileId, request.Audio, request.ContentType, ct);

        var scene = await _unitOfWork.Repository<StoryScene>().FirstOrDefaultAsync(
                        value => value.Id == request.SceneId && value.StoryVersionId == session.StoryVersionId && !value.IsDeleted,
                        cancellationToken: ct)
                    ?? throw new BadRequestException(StorySummaryPolicy.SceneNotInStoryMessage);
        var package = await ParentStoryPackageReader.ForSessionAsync(_unitOfWork, session, ct);
        var manifest = package is null ? null
            : JsonSerializer.Deserialize<ParentPackageManifest>(package.ManifestJson, ParentPackageContext.Json)!;
        if (manifest != null && !manifest.SceneIds.Contains(scene.Id))
            throw new BadRequestException(StorySummaryPolicy.SceneNotInStoryMessage);

        var assets = await StorySummaryIllustrations.LoadAsync(_unitOfWork, session.StoryVersionId,
            session.ParentStoryPackageId, manifest?.MediaAssetIds, [scene.Id], ct);
        var beats = await _unitOfWork.Repository<IllustrationBeat>().FindAsync(
            beat => beat.StorySceneId == scene.Id && !beat.IsDeleted, cancellationToken: ct);
        int? beatId = null;
        MediaAsset? picture;
        if (request.BeatOrder is int order)
        {
            var beat = beats.FirstOrDefault(value => value.BeatOrder == order);
            picture = beat is null ? null : StorySummaryIllustrations.ForBeat(assets, beat.Id, preferCurrent: manifest is null);
            if (picture is null)
                throw new BadRequestException(StorySummaryPolicy.PictureNotInSceneMessage);
            beatId = beat!.Id;
        }
        else
        {
            if (beats.Any(beat => StorySummaryIllustrations.ForBeat(assets, beat.Id) is not null))
                throw new BadRequestException(StorySummaryPolicy.ChoosePictureMessage);
            picture = StorySummaryIllustrations.ForScene(assets, scene.Id);
        }

        var summary = await FindOrNewAsync(session, ct);
        if (summary.Id != 0)
        {
            var asked = await _unitOfWork.Repository<StorySummaryFlag>().FindAsync(
                flag => flag.StorySummaryId == summary.Id && !flag.IsDeleted, cancellationToken: ct);
            if (asked.Any(flag => flag.StorySceneId == scene.Id && flag.IllustrationBeatId == beatId))
                throw new ConflictException(StorySummaryPolicy.AlreadyAskedMessage);
            if (asked.Count >= StorySummaryPolicy.MaxFlags)
                throw new BadRequestException(StorySummaryPolicy.TooManyQuestionsMessage);
        }

        string? path = null;
        string? contentType = null;
        if (hasAudio)
        {
            contentType = request.ContentType!.Split(';')[0].Trim().ToLowerInvariant();
            path = $"story-summaries/{childProfileId}/{sessionId}/questions/{Guid.NewGuid():N}{StorySummaryPolicy.Extension(contentType)}";
            using var content = new MemoryStream(request.Audio);
            await _storage.UploadAsync(path, content, contentType, ct);
        }

        var question = new StorySummaryFlag
        {
            StorySceneId = scene.Id,
            IllustrationBeatId = beatId,
            MediaAssetId = picture?.Id,
            QuestionText = text,
            AudioStoragePath = path,
            AudioContentType = contentType,
            AudioBytes = hasAudio ? request.Audio.Length : null
        };
        var transactionStarted = false;
        try
        {
            await _unitOfWork.BeginTransactionAsync(ct);
            transactionStarted = true;
            summary.FlagsSentAt ??= DateTime.UtcNow;
            await SaveSummaryAsync(summary, ct);
            question.StorySummaryId = summary.Id;
            await _unitOfWork.Repository<StorySummaryFlag>().AddAsync(question, ct);
            await _unitOfWork.SaveChangesAsync(ct);
            await _unitOfWork.CommitTransactionAsync(ct);
        }
        catch
        {
            try
            {
                if (transactionStarted) await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            }
            finally
            {
                if (path != null) await TryDeleteAsync(path);
            }
            throw;
        }

        await NotifyOwnerAsync(session, summary, "question", ct);
        return new StorySummaryQuestionSentDto
        {
            SummaryId = summary.Id, QuestionId = question.Id, Message = StorySummaryPolicy.SentMessage
        };
    }

    private async Task EnsureAudioAsync(int childProfileId, byte[] audio, string? contentType, CancellationToken ct)
    {
        var policy = await _unitOfWork.Repository<SafetyPolicy>().FirstOrDefaultAsync(
            existing => existing.ChildProfileId == childProfileId && !existing.IsDeleted, cancellationToken: ct);
        if (policy is not { VoiceAnswersEnabled: true })
            throw new ForbiddenException(ReadAloudPolicy.VoiceOffMessage);
        if (audio.Length == 0)
            throw new BadRequestException(StorySummaryPolicy.EmptyAudioMessage);
        if (audio.Length > StorySummaryPolicy.MaxAudioBytes)
            throw new BadRequestException(StorySummaryPolicy.AudioTooLargeMessage);
        if (!ReadAloudPolicy.IsSupportedContentType(contentType))
            throw new BadRequestException(ReadAloudPolicy.UnsupportedAudioMessage);
    }

    private async Task<ReadingSession> LoadSessionAsync(int childProfileId, int sessionId, CancellationToken ct)
    {
        var session = await _unitOfWork.Repository<ReadingSession>().GetByIdAsync(sessionId, ct);
        if (session == null || session.IsDeleted || session.ChildProfileId != childProfileId)
            throw new NotFoundException("Phiên đọc", sessionId);
        if (session.Status == SessionStatus.ForceExited || session.ForceExitRequestedAt != null)
            throw new ForbiddenException(ReadingSessionInterruptPolicy.ChildMessage);
        if (!ChildReadingPolicy.IsActive(session.Status) && session.Status != SessionStatus.Completed)
            throw new ConflictException(ReadAloudPolicy.SessionEndedMessage);
        return session;
    }

    private async Task<StorySummary> FindOrNewAsync(ReadingSession session, CancellationToken ct) =>
        await _unitOfWork.Repository<StorySummary>().FirstOrDefaultAsync(
            existing => existing.ReadingSessionId == session.Id && !existing.IsDeleted, cancellationToken: ct)
        ?? new StorySummary
        {
            ReadingSessionId = session.Id,
            ChildProfileId = session.ChildProfileId,
            StoryId = session.StoryId,
            StoryVersionId = session.StoryVersionId
        };

    private async Task SaveSummaryAsync(StorySummary summary, CancellationToken ct)
    {
        var repo = _unitOfWork.Repository<StorySummary>();
        if (summary.Id == 0)
        {
            await repo.AddAsync(summary, ct);
        }
        else
        {
            summary.UpdatedAt = DateTime.UtcNow;
            repo.Update(summary);
        }

        await _unitOfWork.SaveChangesAsync(ct);
    }

    private async Task NotifyOwnerAsync(ReadingSession session, StorySummary summary, string kind, CancellationToken ct)
    {
        try
        {
            var child = await _unitOfWork.Repository<ChildProfile>().GetByIdAsync(session.ChildProfileId, ct);
            if (child == null) return;
            var payload = JsonSerializer.Serialize(new
            {
                childProfileId = child.Id, summaryId = summary.Id, storyId = session.StoryId, kind
            });
            await _notifications.CreateAsync(child.OwnerUserId, NotificationType.StorySummarySent, payload, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Story summary notification failed. SummaryId={SummaryId}, ErrorType={ErrorType}",
                summary.Id, ex.GetType().Name);
        }
    }

    private async Task TryDeleteAsync(string path)
    {
        try
        {
            await _storage.DeleteAsync(path, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Orphan story summary audio could not be deleted. ErrorType={ErrorType}", ex.GetType().Name);
        }
    }

    private static StorySummarySentDto Sent(StorySummary summary) =>
        new() { SummaryId = summary.Id, Message = StorySummaryPolicy.SentMessage };
}
