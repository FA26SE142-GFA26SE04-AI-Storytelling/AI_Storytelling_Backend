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

/// <summary>Trẻ gửi tóm tắt cuối truyện cho chủ hồ sơ: ghi âm và/hoặc đánh cờ cảnh còn thắc mắc. Không dùng AI.</summary>
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
        var policy = await _unitOfWork.Repository<SafetyPolicy>().FirstOrDefaultAsync(
            existing => existing.ChildProfileId == childProfileId && !existing.IsDeleted, cancellationToken: ct);
        if (policy is not { VoiceAnswersEnabled: true })
            throw new ForbiddenException(ReadAloudPolicy.VoiceOffMessage);
        if (request.Audio.Length == 0)
            throw new BadRequestException(StorySummaryPolicy.EmptyAudioMessage);
        if (request.Audio.Length > StorySummaryPolicy.MaxAudioBytes)
            throw new BadRequestException(StorySummaryPolicy.AudioTooLargeMessage);
        if (!ReadAloudPolicy.IsSupportedContentType(request.ContentType))
            throw new BadRequestException(ReadAloudPolicy.UnsupportedAudioMessage);

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

    public async Task<StorySummarySentDto> SendFlagsAsync(
        int childProfileId, int sessionId, StorySummaryFlagsRequestDto request, CancellationToken ct = default)
    {
        var session = await LoadSessionAsync(childProfileId, sessionId, ct);
        var sceneIds = (request.SceneIds ?? new List<int>()).Distinct().ToList();
        if (sceneIds.Count == 0)
            throw new BadRequestException(StorySummaryPolicy.NoFlagsMessage);
        if (sceneIds.Count > StorySummaryPolicy.MaxFlags)
            throw new BadRequestException(StorySummaryPolicy.TooManyFlagsMessage);
        var versionId = session.StoryVersionId;
        var scenes = await _unitOfWork.Repository<StoryScene>().FindAsync(
            scene => sceneIds.Contains(scene.Id) && scene.StoryVersionId == versionId && !scene.IsDeleted,
            cancellationToken: ct);
        if (scenes.Count != sceneIds.Count)
            throw new BadRequestException(StorySummaryPolicy.SceneNotInStoryMessage);

        var summary = await FindOrNewAsync(session, ct);
        if (summary.FlagsSentAt != null)
            throw new ConflictException(StorySummaryPolicy.AlreadyFlaggedMessage);

        await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            summary.FlagsSentAt = DateTime.UtcNow;
            await SaveSummaryAsync(summary, ct);
            await _unitOfWork.Repository<StorySummaryFlag>().AddRangeAsync(
                sceneIds.Select(id => new StorySummaryFlag { StorySummaryId = summary.Id, StorySceneId = id }), ct);
            await _unitOfWork.SaveChangesAsync(ct);
            await _unitOfWork.CommitTransactionAsync(ct);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(ct);
            throw;
        }

        await NotifyOwnerAsync(session, summary, "flags", ct);
        return Sent(summary);
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
