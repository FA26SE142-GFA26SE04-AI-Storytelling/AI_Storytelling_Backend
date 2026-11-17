using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.Interfaces;
using StoryPlatform.Application.Features.MediaStorage.Interfaces;
using StoryPlatform.Application.Features.ReadingSessions.Interfaces;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Application.Features.ReadingSessions.Services;

/// <summary>Chủ hồ sơ nghe / xóa bản ghi đọc to của con. File lưu tới khi chủ hồ sơ xóa.</summary>
public class ParentReadAloudRecordingService : IParentReadAloudRecordingService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IChildProfileAccessGuard _guard;
    private readonly IMediaStorage _storage;

    public ParentReadAloudRecordingService(IUnitOfWork unitOfWork, IChildProfileAccessGuard guard, IMediaStorage storage)
    {
        _unitOfWork = unitOfWork;
        _guard = guard;
        _storage = storage;
    }

    public async Task<ParentReadAloudRecordingsDto> ListAsync(
        int parentUserId, int childProfileId, int sessionId, CancellationToken ct = default)
    {
        await RequireSessionAsync(parentUserId, childProfileId, sessionId, ct);
        var rows = await _unitOfWork.Repository<ReadAloudRecording>().FindAsync(
            row => row.ReadingSessionId == sessionId && !row.IsDeleted, cancellationToken: ct);

        var segmentIds = rows.Where(row => row.StorySegmentId.HasValue).Select(row => row.StorySegmentId!.Value).Distinct().ToList();
        var segments = segmentIds.Count == 0 ? new Dictionary<int, StorySegment>()
            : (await _unitOfWork.Repository<StorySegment>().FindAsync(
                segment => segmentIds.Contains(segment.Id), cancellationToken: ct)).ToDictionary(segment => segment.Id);
        var sceneIds = segments.Values.Select(segment => segment.StorySceneId).Distinct().ToList();
        var scenes = sceneIds.Count == 0 ? new Dictionary<int, StoryScene>()
            : (await _unitOfWork.Repository<StoryScene>().FindAsync(
                scene => sceneIds.Contains(scene.Id), cancellationToken: ct)).ToDictionary(scene => scene.Id);
        var interactionIds = rows.Where(row => row.ListeningInteractionId.HasValue)
            .Select(row => row.ListeningInteractionId!.Value).Distinct().ToList();
        var interactions = interactionIds.Count == 0 ? new Dictionary<int, ListeningInteraction>()
            : (await _unitOfWork.Repository<ListeningInteraction>().FindAsync(
                interaction => interactionIds.Contains(interaction.Id), cancellationToken: ct)).ToDictionary(x => x.Id);

        var items = new List<ParentReadAloudRecordingDto>();
        foreach (var row in rows)
        {
            var segment = row.StorySegmentId is int segmentId ? segments.GetValueOrDefault(segmentId) : null;
            var scene = segment != null ? scenes.GetValueOrDefault(segment.StorySceneId) : null;
            var interaction = row.ListeningInteractionId is int interactionId ? interactions.GetValueOrDefault(interactionId) : null;
            items.Add(new ParentReadAloudRecordingDto
            {
                RecordingId = row.Id,
                Kind = row.StorySegmentId.HasValue ? "Segment" : "Practice",
                SegmentId = row.StorySegmentId,
                InteractionId = row.ListeningInteractionId,
                SceneIndex = scene?.SceneIndex,
                SegmentOrder = segment?.SegmentOrder,
                Text = segment?.TextContent ?? interaction?.SourceQuote ?? string.Empty,
                GoodCount = row.GoodCount,
                RetryCount = row.RetryCount,
                SkippedCount = row.SkippedCount,
                TotalCount = row.TotalCount,
                GoodPercent = QuizRunCalculator.Percent(row.GoodCount, row.TotalCount),
                AudioUrl = await _storage.GetSignedUrlAsync(row.AudioStoragePath, ReadAloudPolicy.ListenUrlLifetime, ct),
                RecordedAt = row.RecordedAt
            });
        }

        return new ParentReadAloudRecordingsDto
        {
            SessionId = sessionId,
            Recordings = items
                .OrderBy(item => item.Kind == "Segment" ? 0 : 1)
                .ThenBy(item => item.SceneIndex ?? int.MaxValue)
                .ThenBy(item => item.SegmentOrder ?? int.MaxValue)
                .ThenBy(item => item.RecordedAt)
                .ThenBy(item => item.RecordingId)
                .ToList()
        };
    }

    public async Task DeleteAsync(int parentUserId, int childProfileId, int sessionId, int recordingId, CancellationToken ct = default)
    {
        await RequireSessionAsync(parentUserId, childProfileId, sessionId, ct);
        var row = await _unitOfWork.Repository<ReadAloudRecording>().FirstOrDefaultAsync(
                      value => value.Id == recordingId && value.ReadingSessionId == sessionId && !value.IsDeleted,
                      cancellationToken: ct)
                  ?? throw new NotFoundException("Bản ghi đọc to", recordingId);

        await RemoveAsync(row, ct);
        await _unitOfWork.SaveChangesAsync(ct);
    }

    public async Task PurgeChildAsync(int childProfileId, CancellationToken ct = default)
    {
        var rows = await _unitOfWork.Repository<ReadAloudRecording>().FindAsync(
            row => row.ChildProfileId == childProfileId && !row.IsDeleted, cancellationToken: ct);
        foreach (var row in rows)
        {
            await RemoveAsync(row, ct);
        }

        await _unitOfWork.SaveChangesAsync(ct);
    }

    private async Task RequireSessionAsync(int parentUserId, int childProfileId, int sessionId, CancellationToken ct)
    {
        await _guard.EnsureOwnerAsync(childProfileId, parentUserId, ct);
        var session = await _unitOfWork.Repository<ReadingSession>().GetByIdAsync(sessionId, ct);
        if (session == null || session.IsDeleted || session.ChildProfileId != childProfileId)
        {
            throw new NotFoundException("Phiên đọc", sessionId);
        }
    }

    private async Task RemoveAsync(ReadAloudRecording row, CancellationToken ct)
    {
        await _storage.DeleteAsync(row.AudioStoragePath, ct);
        row.IsDeleted = true;
        row.UpdatedAt = DateTime.UtcNow;
        _unitOfWork.Repository<ReadAloudRecording>().Update(row);
    }
}
