using System.Text.Json;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ReadingSessions.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ReadingSessions.Services;

/// <summary>
/// Luyện đọc thành tiếng (Luồng 3 mục 4): nhận dạng giọng nói rồi so khớp từng chữ.
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

        var segment = await _unitOfWork.Repository<StorySegment>().GetByIdAsync(segmentId, ct);
        var scene = segment == null || segment.IsDeleted
            ? null
            : await _unitOfWork.Repository<StoryScene>().GetByIdAsync(segment.StorySceneId, ct);
        if (segment == null || scene == null || scene.IsDeleted || scene.StoryVersionId != session.StoryVersionId)
        {
            throw new NotFoundException("Đoạn truyện", segmentId);
        }

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
        var words = ReadAloudMatcher.Match(segment.TextContent, heard);
        var good = words.Count(word => word.Status == ReadAloudWordStatus.Good);
        var retry = words.Count(word => word.Status == ReadAloudWordStatus.Retry);
        var skipped = words.Count - good - retry;

        await _unitOfWork.Repository<TelemetryLog>().AddAsync(new TelemetryLog
        {
            ReadingSessionId = session.Id,
            EventType = TelemetryEventType.AsrWordResult,
            // Chỉ số đếm theo chữ của đoạn truyện; không lưu âm thanh hay lời trẻ nói.
            EventPayload = JsonSerializer.Serialize(new { segmentId, total = words.Count, good, retry, skipped }),
            OccurredAt = DateTime.UtcNow
        }, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return new ReadAloudResultDto
        {
            SessionId = session.Id,
            SegmentId = segmentId,
            Words = words,
            GoodCount = good,
            RetryCount = retry,
            SkippedCount = skipped,
            TotalCount = words.Count,
            Message = ReadAloudPolicy.Message(good, words.Count)
        };
    }
}
