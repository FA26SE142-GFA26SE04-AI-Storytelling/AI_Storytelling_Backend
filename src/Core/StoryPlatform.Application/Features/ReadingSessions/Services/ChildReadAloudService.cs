using System.Text.Json;
using Microsoft.Extensions.Logging;
using StoryPlatform.Application.Features.MediaStorage.Interfaces;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ReadingSessions.Interfaces;
using StoryPlatform.Application.Features.ParentAuthoring;
using StoryPlatform.Application.Features.ListeningInteractions.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ReadingSessions.Services;

/// <summary>
/// Luyện đọc thành tiếng theo đoạn truyện hoặc đoạn luyện; nhận dạng rồi so khớp từng chữ.
/// Giữ bản ghi lần đọc gần nhất cho chủ hồ sơ; telemetry chỉ chứa số đếm. Không giới hạn lượt, không thưởng.
/// </summary>
public class ChildReadAloudService : IChildReadAloudService
{
    public const string VoiceRetryLimitCode = "LISTENING_VOICE_RETRY_LIMIT";
    /// <summary>
    /// Số lần chấm tối đa mỗi tương tác nói/âm chữ của bé 6 tuổi: 1 lần đọc đầu + tối đa 2 lần đọc lại từ vàng
    /// (spec 27/11/2026: "đọc lại tối đa 2 lần"). Lần chấm thứ 4 trả LISTENING_VOICE_RETRY_LIMIT.
    /// </summary>
    public const int MaxAge6PracticeAttempts = 3;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISpeechRecognizer _recognizer;
    private readonly IMediaStorage _storage;
    private readonly ILogger<ChildReadAloudService> _logger;

    public ChildReadAloudService(IUnitOfWork unitOfWork, ISpeechRecognizer recognizer, IMediaStorage storage,
        ILogger<ChildReadAloudService> logger)
    {
        _unitOfWork = unitOfWork;
        _recognizer = recognizer;
        _storage = storage;
        _logger = logger;
    }

    public async Task<ReadAloudResultDto> CheckAsync(
        int childProfileId, int sessionId, int segmentId, ReadAloudRequest request, CancellationToken ct = default)
    {
        var (session, _) = await LoadActiveSessionAsync(childProfileId, sessionId, ct);
        var segment = await _unitOfWork.Repository<StorySegment>().GetByIdAsync(segmentId, ct);
        var scene = segment == null || segment.IsDeleted
            ? null
            : await _unitOfWork.Repository<StoryScene>().GetByIdAsync(segment.StorySceneId, ct);
        if (segment == null || scene == null || scene.IsDeleted || scene.StoryVersionId != session.StoryVersionId)
        {
            throw new NotFoundException("Đoạn truyện", segmentId);
        }

        var result = await ScoreAsync(childProfileId, session, segment.TextContent, segmentId, null, request, ct);
        result.SegmentId = segmentId;
        return result;
    }

    public async Task<ReadAloudResultDto> CheckPracticeAsync(
        int childProfileId, int sessionId, int interactionId, ReadAloudRequest request, CancellationToken ct = default)
    {
        var (session, package) = await LoadActiveSessionAsync(childProfileId, sessionId, ct);
        var manifest = package is null ? null : JsonSerializer.Deserialize<ParentPackageManifest>(package.ManifestJson, ParentPackageContext.Json);
        var profile = await _unitOfWork.Repository<ChildProfile>().GetByIdAsync(childProfileId, ct);
        if (profile is null || profile.IsDeleted) throw new NotFoundException("Hồ sơ trẻ", childProfileId);
        var childAge = ListeningAgeSets.AgeFor(profile.Age, profile.AgeBand);
        var interaction = await _unitOfWork.Repository<ListeningInteraction>().GetByIdAsync(interactionId, ct);
        if (interaction == null || interaction.IsDeleted || interaction.TargetAge != childAge
            || interaction.Type is not (ListeningInteractionType.ReadAloud or ListeningInteractionType.RolePlay or ListeningInteractionType.CharacterLetter)
            || interaction.StoryVersionId != session.StoryVersionId
            || interaction.ParentStoryPackageId != session.ParentStoryPackageId
            || (package is not null && manifest?.ListeningIds.Contains(interactionId) != true))
        {
            throw new NotFoundException("Đoạn luyện đọc", interactionId);
        }

        string? target = interaction.SourceQuote;
        if (interaction.Type == ListeningInteractionType.CharacterLetter)
        {
            var options = await _unitOfWork.Repository<ListeningInteractionOption>().FindAsync(
                option => option.ListeningInteractionId == interactionId && !option.IsDeleted, cancellationToken: ct);
            var letter = options.SingleOrDefault(option => option.IsMatch)?.Text.Trim();
            target = letter is null ? null : VietnameseLetterUnits.SoundOf(letter);
        }
        if (string.IsNullOrWhiteSpace(target)) throw new NotFoundException("Đoạn luyện đọc", interactionId);

        if (interaction.Type == ListeningInteractionType.ReadAloud)
        {
            var scene = interaction.StorySceneId is int sceneId
                ? await _unitOfWork.Repository<StoryScene>().GetByIdAsync(sceneId, ct) : null;
            if (scene is null || scene.IsDeleted || scene.StoryVersionId != session.StoryVersionId
                || (package is not null && manifest?.SceneIds.Contains(scene.Id) != true))
                throw new NotFoundException("Đoạn luyện đọc", interactionId);

            var allowedAssets = manifest?.MediaAssetIds.ToList() ?? [];
            var assets = await _unitOfWork.Repository<MediaAsset>().FindAsync(
                asset => asset.StoryVersionId == session.StoryVersionId
                         && asset.ParentStoryPackageId == session.ParentStoryPackageId
                         && asset.ListeningInteractionId == interactionId
                         && (package == null || allowedAssets.Contains(asset.Id)) && !asset.IsDeleted, cancellationToken: ct);
            IReadOnlyList<MediaAsset> practiceAssets = package is null ? assets
                : assets.Select(asset => ParentStoryPackageReader.ProxyAsset(asset, session.FamilyConfirmationId!.Value)).ToArray();
            if (ChildListeningContent.BuildReadAloud(childAge, [interaction], practiceAssets) is null)
                throw new NotFoundException("Đoạn luyện đọc", interactionId);
        }

        var softFeedback = interaction.TargetAge == 6 && interaction.Type is ListeningInteractionType.RolePlay or ListeningInteractionType.CharacterLetter;
        var result = await ScoreAsync(childProfileId, session, target, null, interactionId, request, ct, softFeedback);
        if (softFeedback)
            result.Message = result.RetryCount + result.SkippedCount > 0
                ? ListeningAudioParts.FixedLine(ListeningAudioParts.Unclear) : interaction.MatchFeedback ?? string.Empty;
        result.InteractionId = interactionId;
        return result;
    }

    private async Task<(ReadingSession Session, ParentStoryPackage? Package)> LoadActiveSessionAsync(int childProfileId, int sessionId, CancellationToken ct)
    {
        var session = await _unitOfWork.Repository<ReadingSession>().GetByIdAsync(sessionId, ct);
        if (session == null || session.IsDeleted || session.ChildProfileId != childProfileId)
        {
            throw new NotFoundException("Phiên đọc", sessionId);
        }

        if (session.Status == SessionStatus.ForceExited || session.ForceExitRequestedAt != null)
        {
            throw new ForbiddenException(ReadingSessionInterruptPolicy.ChildMessage);
        }

        if (!ChildReadingPolicy.IsActive(session.Status))
        {
            throw new ConflictException(ReadAloudPolicy.SessionEndedMessage);
        }

        var package = await ParentStoryPackageReader.ForSessionAsync(_unitOfWork, session, ct);
        return (session, package);
    }

    // Kiểm quyền giọng nói và file, nhận dạng, so khớp; ghi telemetry số đếm và bản ghi lần đọc gần nhất.
    private async Task<ReadAloudResultDto> ScoreAsync(int childProfileId, ReadingSession session, string text,
        int? segmentId, int? interactionId, ReadAloudRequest request, CancellationToken ct, bool softFeedback = false)
    {
        var policy = await _unitOfWork.Repository<SafetyPolicy>().FirstOrDefaultAsync(
            existing => existing.ChildProfileId == childProfileId && !existing.IsDeleted, cancellationToken: ct);
        if (policy is not { VoiceAnswersEnabled: true })
        {
            throw new ForbiddenException(ReadAloudPolicy.VoiceOffMessage);
        }

        if (request.Audio.Length == 0)
        {
            throw new BadRequestException(ReadAloudPolicy.EmptyAudioMessage);
        }

        if (request.Audio.Length > ReadAloudPolicy.MaxAudioBytes)
        {
            throw new BadRequestException(ReadAloudPolicy.AudioTooLargeMessage);
        }

        if (!ReadAloudPolicy.IsSupportedContentType(request.ContentType))
        {
            throw new BadRequestException(ReadAloudPolicy.UnsupportedAudioMessage);
        }

        var heard = await _recognizer.RecognizeAsync(request.Audio, ct);
        var words = ReadAloudMatcher.Match(text, heard);
        if (softFeedback)
            words = words.Select(word => word.Status == ReadAloudWordStatus.Skipped
                ? word with { Status = ReadAloudWordStatus.Retry, Hint = ReadAloudHint.Unclear } : word).ToArray();
        var good = words.Count(word => word.Status == ReadAloudWordStatus.Good);
        var retry = words.Count(word => word.Status == ReadAloudWordStatus.Retry);
        var skipped = words.Count - good - retry;

        var targetKey = segmentId.HasValue ? "segmentId" : "interactionId";
        var targetId = segmentId ?? interactionId!.Value;
        var newPath = await TryUploadAsync(session, segmentId, interactionId, request, ct);
        string? oldPath = null;
        await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            // Bé bấm gửi hai lần cùng lúc: xếp hàng theo phiên rồi mới tìm bản ghi, để không thêm trùng dòng
            // (unique index) và không để file của lần "thua" nằm lại không dòng nào trỏ tới.
            // Dùng chung khóa advisory với id truyện; trùng số chỉ chờ một transaction ngắn.
            await _unitOfWork.AcquireTransactionLockAsync(session.Id, ct);
            if (softFeedback)
            {
                var previous = await _unitOfWork.Repository<TelemetryLog>().FindAsync(
                    log => log.ReadingSessionId == session.Id && log.EventType == TelemetryEventType.AsrWordResult && !log.IsDeleted,
                    cancellationToken: ct);
                if (previous.Count(log => IsPracticeResultFor(log.EventPayload, interactionId!.Value)) >= MaxAge6PracticeAttempts)
                    throw new ConflictException(VoiceRetryLimitCode);
            }
            await _unitOfWork.Repository<TelemetryLog>().AddAsync(new TelemetryLog
            {
                ReadingSessionId = session.Id,
                EventType = TelemetryEventType.AsrWordResult,
                // Chỉ số đếm theo chữ; không ghi lời trẻ nói vào telemetry. targetKey = segmentId hoặc interactionId.
                EventPayload = JsonSerializer.Serialize(new Dictionary<string, int>
                {
                    [targetKey] = targetId, ["total"] = words.Count, ["good"] = good, ["retry"] = retry, ["skipped"] = skipped
                }),
                OccurredAt = DateTime.UtcNow
            }, ct);

            if (newPath != null)
            {
                oldPath = await StageRecordingAsync(session, segmentId, interactionId, request, newPath,
                    (good, retry, skipped, words.Count), words.Select(word => word.Status).ToArray(), ct);
            }

            await _unitOfWork.SaveChangesAsync(ct);
            await _unitOfWork.CommitTransactionAsync(ct);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            if (newPath != null) await TryDeleteAsync(newPath);
            throw;
        }

        if (oldPath != null) await TryDeleteAsync(oldPath);

        return new ReadAloudResultDto
        {
            SessionId = session.Id,
            Words = words,
            GoodCount = good,
            RetryCount = retry,
            SkippedCount = skipped,
            TotalCount = words.Count,
            Message = ReadAloudPolicy.Message(good, words.Count)
        };
    }

    private static bool IsPracticeResultFor(string? payload, int interactionId)
    {
        if (string.IsNullOrWhiteSpace(payload)) return false;
        try
        {
            using var json = JsonDocument.Parse(payload);
            return json.RootElement.ValueKind == JsonValueKind.Object
                && json.RootElement.TryGetProperty("interactionId", out var target)
                && target.ValueKind == JsonValueKind.Number
                && target.TryGetInt32(out var id) && id == interactionId;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Upload file; lỗi kho lưu trữ không làm hỏng việc chấm của bé (trả null, ghi log).</summary>
    private async Task<string?> TryUploadAsync(ReadingSession session, int? segmentId, int? interactionId,
        ReadAloudRequest request, CancellationToken ct)
    {
        var contentType = request.ContentType!.Split(';')[0].Trim().ToLowerInvariant();
        var target = segmentId is int id ? $"segment-{id}" : $"practice-{interactionId}";
        var path = $"{ReadAloudPolicy.StorageFolder}/{session.ChildProfileId}/{session.Id}/{target}/"
                   + $"{Guid.NewGuid():N}{StorySummaryPolicy.Extension(contentType)}";
        try
        {
            using var content = new MemoryStream(request.Audio);
            await _storage.UploadAsync(path, content, contentType, ct);
            return path;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("Read-aloud recording upload failed. SessionId={SessionId}, ErrorType={ErrorType}",
                session.Id, ex.GetType().Name);
            return null;
        }
    }

    /// <summary>Thêm hoặc thay bản ghi của đoạn (chưa lưu); trả đường dẫn file cũ cần xóa sau khi lưu.</summary>
    private async Task<string?> StageRecordingAsync(ReadingSession session, int? segmentId, int? interactionId,
        ReadAloudRequest request, string path, (int Good, int Retry, int Skipped, int Total) score,
        IReadOnlyList<string> statuses, CancellationToken ct)
    {
        var repo = _unitOfWork.Repository<ReadAloudRecording>();
        var existing = await repo.FirstOrDefaultAsync(
            row => row.ReadingSessionId == session.Id && row.StorySegmentId == segmentId
                   && row.ListeningInteractionId == interactionId && !row.IsDeleted, cancellationToken: ct);
        var recording = existing ?? new ReadAloudRecording
        {
            ReadingSessionId = session.Id,
            ChildProfileId = session.ChildProfileId,
            StorySegmentId = segmentId,
            ListeningInteractionId = interactionId
        };
        var oldPath = existing?.AudioStoragePath;
        recording.AudioStoragePath = path;
        recording.AudioContentType = request.ContentType!.Split(';')[0].Trim().ToLowerInvariant();
        recording.AudioBytes = request.Audio.Length;
        (recording.GoodCount, recording.RetryCount, recording.SkippedCount, recording.TotalCount) = score;
        recording.RecordedAt = DateTime.UtcNow;
        recording.WordStatusesJson = JsonSerializer.Serialize(statuses.Select(status => status switch
        {
            ReadAloudWordStatus.Good => "Good",
            ReadAloudWordStatus.Retry => "Retry",
            _ => "Skipped"
        }));
        if (existing == null)
        {
            await repo.AddAsync(recording, ct);
        }
        else
        {
            recording.UpdatedAt = DateTime.UtcNow;
            repo.Update(recording);
        }

        return oldPath;
    }

    private async Task TryDeleteAsync(string path)
    {
        try
        {
            await _storage.DeleteAsync(path, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Read-aloud recording file could not be deleted. ErrorType={ErrorType}", ex.GetType().Name);
        }
    }
}
