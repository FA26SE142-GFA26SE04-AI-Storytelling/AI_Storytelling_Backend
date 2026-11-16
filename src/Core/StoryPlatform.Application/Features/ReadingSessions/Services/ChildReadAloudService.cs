using System.Text.Json;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ReadingSessions.Interfaces;
using StoryPlatform.Application.Features.ParentAuthoring;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ReadingSessions.Services;

/// <summary>
/// Luyện đọc thành tiếng theo đoạn truyện hoặc đoạn luyện; nhận dạng rồi so khớp từng chữ.
/// Không lưu âm thanh; telemetry chỉ chứa số đếm. Không giới hạn lượt, không thưởng.
/// </summary>
public class ChildReadAloudService : IChildReadAloudService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISpeechRecognizer _recognizer;

    public ChildReadAloudService(IUnitOfWork unitOfWork, ISpeechRecognizer recognizer)
    {
        _unitOfWork = unitOfWork;
        _recognizer = recognizer;
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

        var result = await ScoreAsync(childProfileId, session, segment.TextContent, "segmentId", segmentId, request, ct);
        result.SegmentId = segmentId;
        return result;
    }

    public async Task<ReadAloudResultDto> CheckPracticeAsync(
        int childProfileId, int sessionId, int interactionId, ReadAloudRequest request, CancellationToken ct = default)
    {
        var (session, package) = await LoadActiveSessionAsync(childProfileId, sessionId, ct);
        var manifest = package is null ? null : JsonSerializer.Deserialize<ParentPackageManifest>(package.ManifestJson, ParentPackageContext.Json);
        var interaction = await _unitOfWork.Repository<ListeningInteraction>().GetByIdAsync(interactionId, ct);
        if (interaction == null || interaction.IsDeleted || interaction.Type != ListeningInteractionType.ReadAloud
            || interaction.StoryVersionId != session.StoryVersionId
            || interaction.ParentStoryPackageId != session.ParentStoryPackageId
            || string.IsNullOrWhiteSpace(interaction.SourceQuote)
            || (package is not null && manifest?.ListeningIds.Contains(interactionId) != true))
        {
            throw new NotFoundException("Đoạn luyện đọc", interactionId);
        }

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
        if (ChildListeningContent.BuildReadAloud([interaction], practiceAssets) is null)
            throw new NotFoundException("Đoạn luyện đọc", interactionId);

        var result = await ScoreAsync(childProfileId, session, interaction.SourceQuote, "interactionId", interactionId, request, ct);
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

    // Kiểm quyền giọng nói và file, nhận dạng, so khớp, ghi telemetry chỉ gồm số đếm.
    private async Task<ReadAloudResultDto> ScoreAsync(int childProfileId, ReadingSession session, string text,
        string targetKey, int targetId, ReadAloudRequest request, CancellationToken ct)
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
        var good = words.Count(word => word.Status == ReadAloudWordStatus.Good);
        var retry = words.Count(word => word.Status == ReadAloudWordStatus.Retry);
        var skipped = words.Count - good - retry;

        await _unitOfWork.Repository<TelemetryLog>().AddAsync(new TelemetryLog
        {
            ReadingSessionId = session.Id,
            EventType = TelemetryEventType.AsrWordResult,
            // Chỉ số đếm theo chữ; không lưu âm thanh hay lời trẻ nói. targetKey = segmentId hoặc interactionId.
            EventPayload = JsonSerializer.Serialize(new Dictionary<string, int>
            {
                [targetKey] = targetId, ["total"] = words.Count, ["good"] = good, ["retry"] = retry, ["skipped"] = skipped
            }),
            OccurredAt = DateTime.UtcNow
        }, ct);
        await _unitOfWork.SaveChangesAsync(ct);

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
}
