using System.Text.Json;
using Microsoft.Extensions.Logging;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.MediaStorage.Interfaces;
using StoryPlatform.Application.Features.ParentAuthoring;
using StoryPlatform.Application.Features.ReadingSessions.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ReadingSessions.Services;

/// <summary>Trẻ chọn cảm xúc, cảnh thích nhất và gửi lời về cảnh đó cho chủ hồ sơ. Không dùng AI.</summary>
public class ChildStoryReflectionService : IChildStoryReflectionService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMediaStorage _storage;
    private readonly ILogger<ChildStoryReflectionService> _logger;

    public ChildStoryReflectionService(IUnitOfWork unitOfWork, IMediaStorage storage, ILogger<ChildStoryReflectionService> logger)
    {
        _unitOfWork = unitOfWork;
        _storage = storage;
        _logger = logger;
    }

    public async Task<ChildStoryReflectionDto> GetAsync(int childProfileId, int sessionId, CancellationToken ct = default)
    {
        var session = await _unitOfWork.Repository<ReadingSession>().GetByIdAsync(sessionId, ct);
        if (session == null || session.IsDeleted || session.ChildProfileId != childProfileId)
            throw new NotFoundException("Phiên đọc", sessionId);

        var reflection = await FindAsync(session.Id, ct);
        return await ToDtoAsync(session, reflection, null, ct);
    }

    public async Task<ChildStoryReflectionDto> SetEmotionsAsync(
        int childProfileId, int sessionId, SetStoryEmotionsRequestDto request, CancellationToken ct = default)
    {
        var codes = request.Emotions ?? new List<string>();
        if (codes.Count > StoryReflectionPolicy.MaxEmotions)
            throw new BadRequestException(StoryReflectionPolicy.TooManyEmotionsMessage);
        var emotions = new List<StoryEmotion>();
        foreach (var code in codes)
        {
            if (!StoryEmotionLabels.TryParse(code, out var emotion) || emotions.Contains(emotion))
                throw new BadRequestException(StoryReflectionPolicy.ChooseEmotionMessage);
            emotions.Add(emotion);
        }
        if (emotions.Count == 0)
            throw new BadRequestException(StoryReflectionPolicy.ChooseEmotionMessage);

        var session = await LoadFinishedSessionAsync(childProfileId, sessionId, ct);
        var reflection = await InSessionLockAsync(session.Id, async () =>
        {
            var row = await FindAsync(session.Id, ct) ?? New(session);
            row.Emotions = emotions.Select(emotion => emotion.ToString()).ToList();
            await SaveAsync(row, ct);
            return row;
        }, ct);
        return await ToDtoAsync(session, reflection, null, ct);
    }

    public async Task<ChildStoryReflectionDto> SetFavoriteSceneAsync(
        int childProfileId, int sessionId, SetFavoriteSceneRequestDto request, CancellationToken ct = default)
    {
        var session = await LoadFinishedSessionAsync(childProfileId, sessionId, ct);
        var scene = await _unitOfWork.Repository<StoryScene>().FirstOrDefaultAsync(
                        value => value.Id == request.SceneId && value.StoryVersionId == session.StoryVersionId && !value.IsDeleted,
                        cancellationToken: ct)
                    ?? throw new BadRequestException(StoryReflectionPolicy.SceneNotInStoryMessage);
        var package = await ParentStoryPackageReader.ForSessionAsync(_unitOfWork, session, ct);
        var manifest = package is null ? null
            : JsonSerializer.Deserialize<ParentPackageManifest>(package.ManifestJson, ParentPackageContext.Json)!;
        if (manifest != null && !manifest.SceneIds.Contains(scene.Id))
            throw new BadRequestException(StoryReflectionPolicy.SceneNotInStoryMessage);

        var assets = await StorySummaryIllustrations.LoadAsync(_unitOfWork, session.StoryVersionId,
            session.ParentStoryPackageId, manifest?.MediaAssetIds, [scene.Id], ct);
        var pictureId = StorySummaryIllustrations.ForScene(assets, scene.Id)?.Id;
        var (reflection, oldAudio) = await InSessionLockAsync(session.Id, async () =>
        {
            var row = await FindAsync(session.Id, ct) ?? New(session);
            if (row.FavoriteSceneId == scene.Id)
                return (row, (string?)null);

            var previousAudio = row.NoteAudioStoragePath;
            row.FavoriteSceneId = scene.Id;
            row.FavoriteMediaAssetId = pictureId;
            ClearNote(row);
            await SaveAsync(row, ct);
            return (row, previousAudio);
        }, ct);
        if (oldAudio != null) await TryDeleteAsync(oldAudio);
        return await ToDtoAsync(session, reflection, null, ct);
    }

    public async Task<ChildStoryReflectionDto> SendNoteAsync(
        int childProfileId, int sessionId, StoryReflectionNoteRequest request, CancellationToken ct = default)
    {
        var session = await LoadFinishedSessionAsync(childProfileId, sessionId, ct);
        // Kiểm sớm để không upload khi bé chưa chọn cảnh; kiểm lại dưới khóa trước khi lưu.
        var sceneId = (await FindAsync(session.Id, ct))?.FavoriteSceneId
                      ?? throw new BadRequestException(StoryReflectionPolicy.ChooseSceneFirstMessage);

        var text = StoryReflectionPolicy.NormalizeNote(request.Text);
        if (text is { Length: > StoryReflectionPolicy.MaxNoteTextLength })
            throw new BadRequestException(StoryReflectionPolicy.NoteTooLongMessage);
        var hasAudio = request.Audio.Length > 0;
        if (text == null && !hasAudio)
            throw new BadRequestException(StoryReflectionPolicy.EmptyNoteMessage);

        string? path = null;
        string? contentType = null;
        if (hasAudio)
        {
            await EnsureAudioAsync(childProfileId, request.Audio, request.ContentType, ct);
            contentType = request.ContentType!.Split(';')[0].Trim().ToLowerInvariant();
            path = $"{StoryReflectionPolicy.StorageFolder}/{childProfileId}/{sessionId}/{Guid.NewGuid():N}{StorySummaryPolicy.Extension(contentType)}";
            using var content = new MemoryStream(request.Audio);
            await _storage.UploadAsync(path, content, contentType, ct);
        }

        StoryReflection reflection;
        string? oldAudio;
        try
        {
            (reflection, oldAudio) = await InSessionLockAsync(session.Id, async () =>
            {
                var row = await FindAsync(session.Id, ct);
                if (row?.FavoriteSceneId == null)
                    throw new BadRequestException(StoryReflectionPolicy.ChooseSceneFirstMessage);
                // Bé đổi cảnh trong lúc lời đang upload: không gắn lời của cảnh cũ vào cảnh mới.
                if (row.FavoriteSceneId != sceneId)
                    throw new ConflictException(StoryReflectionPolicy.SceneChangedMessage);

                var previousAudio = row.NoteAudioStoragePath;
                row.NoteText = text;
                row.NoteAudioStoragePath = path;
                row.NoteAudioContentType = contentType;
                row.NoteAudioBytes = hasAudio ? request.Audio.Length : null;
                row.NoteSentAt = DateTime.UtcNow;
                await SaveAsync(row, ct);
                return (row, previousAudio);
            }, ct);
        }
        catch
        {
            if (path != null) await TryDeleteAsync(path);
            throw;
        }

        if (oldAudio != null) await TryDeleteAsync(oldAudio);
        return await ToDtoAsync(session, reflection, StoryReflectionPolicy.NoteSentMessage, ct);
    }

    /// <summary>
    /// Đọc–sửa–lưu bản cảm nhận dưới khóa của phiên: bé bấm gửi hai lần cùng lúc thì request sau chờ request trước,
    /// nên không thêm trùng dòng (unique index) và không để file ghi âm của lần "thua" nằm lại không dòng nào trỏ tới.
    /// Dùng chung khóa advisory với id truyện; trùng số chỉ chờ một transaction ngắn.
    /// </summary>
    private async Task<T> InSessionLockAsync<T>(int sessionId, Func<Task<T>> work, CancellationToken ct)
    {
        await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            await _unitOfWork.AcquireTransactionLockAsync(sessionId, ct);
            var result = await work();
            await _unitOfWork.CommitTransactionAsync(ct);
            return result;
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task EnsureAudioAsync(int childProfileId, byte[] audio, string? contentType, CancellationToken ct)
    {
        var policy = await _unitOfWork.Repository<SafetyPolicy>().FirstOrDefaultAsync(
            existing => existing.ChildProfileId == childProfileId && !existing.IsDeleted, cancellationToken: ct);
        if (policy is not { VoiceAnswersEnabled: true })
            throw new ForbiddenException(ReadAloudPolicy.VoiceOffMessage);
        if (audio.Length > StorySummaryPolicy.MaxAudioBytes)
            throw new BadRequestException(StorySummaryPolicy.AudioTooLargeMessage);
        if (!ReadAloudPolicy.IsSupportedContentType(contentType))
            throw new BadRequestException(ReadAloudPolicy.UnsupportedAudioMessage);
    }

    private async Task<ReadingSession> LoadFinishedSessionAsync(int childProfileId, int sessionId, CancellationToken ct)
    {
        var session = await _unitOfWork.Repository<ReadingSession>().GetByIdAsync(sessionId, ct);
        if (session == null || session.IsDeleted || session.ChildProfileId != childProfileId)
            throw new NotFoundException("Phiên đọc", sessionId);
        if (session.ForceExitRequestedAt != null || session.Status == SessionStatus.ForceExited)
            throw new ForbiddenException(ReadingSessionInterruptPolicy.ChildMessage);
        if (session.Status != SessionStatus.Completed)
            throw new ConflictException(ChildReadingPolicy.FeelingAfterReadingMessage);
        return session;
    }

    private Task<StoryReflection?> FindAsync(int sessionId, CancellationToken ct) =>
        _unitOfWork.Repository<StoryReflection>().FirstOrDefaultAsync(
            existing => existing.ReadingSessionId == sessionId && !existing.IsDeleted, cancellationToken: ct);

    private static StoryReflection New(ReadingSession session) => new()
    {
        ReadingSessionId = session.Id,
        ChildProfileId = session.ChildProfileId,
        StoryId = session.StoryId,
        StoryVersionId = session.StoryVersionId
    };

    private static void ClearNote(StoryReflection reflection)
    {
        reflection.NoteText = null;
        reflection.NoteAudioStoragePath = null;
        reflection.NoteAudioContentType = null;
        reflection.NoteAudioBytes = null;
        reflection.NoteSentAt = null;
    }

    private async Task SaveAsync(StoryReflection reflection, CancellationToken ct)
    {
        var repo = _unitOfWork.Repository<StoryReflection>();
        if (reflection.Id == 0)
        {
            await repo.AddAsync(reflection, ct);
        }
        else
        {
            reflection.UpdatedAt = DateTime.UtcNow;
            repo.Update(reflection);
        }

        await _unitOfWork.SaveChangesAsync(ct);
    }

    private async Task<ChildStoryReflectionDto> ToDtoAsync(
        ReadingSession session, StoryReflection? reflection, string? message, CancellationToken ct)
    {
        var policy = await _unitOfWork.Repository<SafetyPolicy>().FirstOrDefaultAsync(
            existing => existing.ChildProfileId == session.ChildProfileId && !existing.IsDeleted, cancellationToken: ct);
        return new ChildStoryReflectionDto
        {
            SessionId = session.Id,
            Emotions = ToEmotionDtos(reflection?.Emotions),
            FavoriteSceneId = reflection?.FavoriteSceneId,
            NoteText = reflection?.NoteText,
            HasNoteRecording = reflection?.NoteAudioStoragePath != null,
            CanRecord = policy is { VoiceAnswersEnabled: true },
            Message = message
        };
    }

    internal static List<StoryEmotionDto> ToEmotionDtos(IEnumerable<string>? codes) =>
        (codes ?? Enumerable.Empty<string>())
            .Select(code => StoryEmotionLabels.TryParse(code, out var emotion)
                ? new StoryEmotionDto { Code = emotion.ToString(), Label = StoryEmotionLabels.Label(emotion) }
                : null)
            .Where(dto => dto != null)
            .Select(dto => dto!)
            .ToList();

    private async Task TryDeleteAsync(string path)
    {
        try
        {
            await _storage.DeleteAsync(path, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Story reflection audio could not be deleted. ErrorType={ErrorType}", ex.GetType().Name);
        }
    }
}
